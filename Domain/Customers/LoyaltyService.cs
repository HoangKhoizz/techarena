using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Customers;

/// <summary>
/// Tích điểm + cập nhật hạng thẻ khi đơn hoàn thành.
/// Công thức: 1 điểm / 10.000₫ (tối thiểu 1 nếu đơn &gt; 0).
/// </summary>
public sealed class LoyaltyService
{
    private readonly PcStoreDbContext _db;

    public LoyaltyService(PcStoreDbContext db) => _db = db;

    public static int ComputePoints(decimal orderTotalVnd)
    {
        if (orderTotalVnd <= 0) return 0;
        var pts = (int)(orderTotalVnd / 10_000m);
        return Math.Max(1, pts);
    }

    /// <summary>
    /// Cộng điểm cho khách gắn với đơn khi đơn lần đầu chuyển sang trạng thái hoàn thành.
    /// Idempotent nhờ cờ Order.LoyaltyPointsAwarded.
    /// </summary>
    public async Task<LoyaltyAwardResult> TryAwardForOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null)
            return LoyaltyAwardResult.Skip("Không tìm thấy đơn.");

        if (order.LoyaltyPointsAwarded)
            return LoyaltyAwardResult.Skip("Đơn đã cộng điểm trước đó.");

        if (order.CustomerId is not int customerId)
            return LoyaltyAwardResult.Skip("Đơn khách vãng lai — không cộng điểm.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (!IsAwardableStatus(status))
            return LoyaltyAwardResult.Skip("Trạng thái đơn chưa đủ điều kiện cộng điểm.");

        var customer = await _db.Customers.SingleOrDefaultAsync(x => x.CustomerId == customerId, ct);
        if (customer is null)
            return LoyaltyAwardResult.Skip("Không tìm thấy khách hàng.");

        var points = ComputePoints(order.TotalPriceVnd);
        if (points <= 0)
            return LoyaltyAwardResult.Skip("Điểm tính được = 0.");

        customer.LoyaltyPoints += points;
        customer.MembershipTier = MembershipTierCatalog.ResolveFromPoints(customer.LoyaltyPoints);
        order.LoyaltyPointsAwarded = true;

        await _db.SaveChangesAsync(ct);
        return LoyaltyAwardResult.Ok(points, customer.LoyaltyPoints, customer.MembershipTier);
    }

    public void RefreshTier(CustomerEntity customer)
    {
        customer.MembershipTier = MembershipTierCatalog.ResolveFromPoints(customer.LoyaltyPoints);
    }

    public static bool IsAwardableStatus(string? statusCode)
    {
        var n = OrderStatusCatalog.Normalize(statusCode);
        return n is OrderStatusCatalog.PaidCompleted
            or OrderStatusCatalog.DeliveredCollected
            or OrderStatusCatalog.DeliveredUnpaid
            or OrderStatusCatalog.Paid;
    }
}

public sealed record LoyaltyAwardResult(bool Awarded, int PointsAdded, int TotalPoints, string Tier, string? SkipReason)
{
    public static LoyaltyAwardResult Ok(int added, int total, string tier) =>
        new(true, added, total, tier, null);

    public static LoyaltyAwardResult Skip(string reason) =>
        new(false, 0, 0, MembershipTierCatalog.None, reason);
}
