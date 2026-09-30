using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Cskh;

public sealed record ReviewEligibility(
    bool CanReview,
    string? Reason,
    bool IsAuthenticated,
    bool HasPurchasedAndReceived,
    bool AlreadyReviewed);

/// <summary>Chỉ khách đã mua + nhận hàng thành công mới được đánh giá; 1 lần / sản phẩm.</summary>
public sealed class ReviewEligibilityService
{
    private readonly PcStoreDbContext _db;

    public ReviewEligibilityService(PcStoreDbContext db) => _db = db;

    public async Task<ReviewEligibility> CheckAsync(int productId, int? customerId, CancellationToken ct = default)
    {
        if (customerId is null or <= 0)
        {
            return new ReviewEligibility(
                false,
                "Vui lòng đăng nhập. Chỉ khách đã mua và nhận hàng thành công mới được đánh giá.",
                false, false, false);
        }

        var already = await _db.ProductReviews.AsNoTracking()
            .AnyAsync(x => x.ProductId == productId && x.CustomerId == customerId, ct);
        if (already)
        {
            return new ReviewEligibility(
                false,
                "Bạn đã đánh giá sản phẩm này rồi (mỗi tài khoản chỉ 1 lần / sản phẩm).",
                true, true, true);
        }

        var purchased = await HasReceivedProductAsync(productId, customerId.Value, ct);
        if (!purchased)
        {
            return new ReviewEligibility(
                false,
                "Chỉ khách đã mua và nhận hàng thành công sản phẩm này mới được đánh giá.",
                true, false, false);
        }

        return new ReviewEligibility(true, null, true, true, false);
    }

    public async Task<bool> HasReceivedProductAsync(int productId, int customerId, CancellationToken ct = default)
    {
        var statusCodes = await (
            from o in _db.Orders.AsNoTracking()
            join oi in _db.OrderItems.AsNoTracking() on o.OrderId equals oi.OrderId
            where o.CustomerId == customerId && oi.ComponentId == productId
            select o.StatusCode
        ).ToListAsync(ct);

        return statusCodes.Any(OrderStatusCatalog.IsDeliveredSuccess);
    }
}
