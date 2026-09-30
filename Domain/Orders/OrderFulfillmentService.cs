using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Orders;

/// <summary>Quy trình duyệt → phân công → giao → thu công nợ (Admin Phase 5).</summary>
public sealed class OrderFulfillmentService
{
    private readonly PcStoreDbContext _db;
    private readonly InventoryStockService _stock;
    private readonly LoyaltyService _loyalty;
    private readonly OrderTimelineService _timeline;

    public OrderFulfillmentService(
        PcStoreDbContext db,
        InventoryStockService stock,
        LoyaltyService loyalty,
        OrderTimelineService timeline)
    {
        _db = db;
        _stock = stock;
        _loyalty = loyalty;
        _timeline = timeline;
    }

    public async Task<(bool Ok, string Message)> ApproveAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status is not OrderStatusCatalog.PendingConfirmation and not OrderStatusCatalog.WaitingPayment)
            return (false, "Chỉ duyệt đơn đang chờ xác nhận / chờ thanh toán.");

        // MoMo chờ thanh toán: chỉ duyệt khi đã PAID — admin vẫn có thể chuyển sang APPROVED nếu đã biết khách trả.
        order.StatusCode = OrderStatusCatalog.Approved;
        _timeline.AppendPending(orderId, OrderStatusCatalog.Approved, note: "Đơn đã được duyệt.");
        await _db.SaveChangesAsync(ct);
        return (true, $"Đã duyệt đơn #{orderId}.");
    }

    public async Task<(bool Ok, string Message)> AssignAndShipAsync(
        int orderId,
        int staffId,
        CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status is not OrderStatusCatalog.Approved and not OrderStatusCatalog.PendingConfirmation
            and not OrderStatusCatalog.Shipping and not OrderStatusCatalog.Packed)
            return (false, "Chỉ phân công đơn đã duyệt / đóng gói / đang giao.");

        var staff = await _db.Staffs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StaffId == staffId && x.IsActive, ct);
        if (staff is null) return (false, "Nhân viên không hợp lệ hoặc đã khóa.");

        order.AssignedStaffId = staffId;
        order.StatusCode = OrderStatusCatalog.Shipping;
        _timeline.AppendPending(orderId, OrderStatusCatalog.Shipping,
            note: $"Phân công giao cho {staff.FullName}.");
        await _db.SaveChangesAsync(ct);
        return (true, $"Đã phân công {staff.FullName} giao đơn #{orderId}.");
    }

    public async Task<(bool Ok, string Message)> ConfirmDeliveryAsync(
        int orderId,
        string outcome,
        decimal? depositAmountVnd,
        CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status is not OrderStatusCatalog.Shipping and not OrderStatusCatalog.Approved
            and not OrderStatusCatalog.Packed)
            return (false, "Chỉ xác nhận giao cho đơn đang giao / đã duyệt / đóng gói.");

        var previous = status;
        var key = (outcome ?? "").Trim().ToUpperInvariant();
        switch (key)
        {
            case "COLLECTED":
                order.StatusCode = OrderStatusCatalog.DeliveredCollected;
                order.PaymentCollectionStatus = PaymentCollectionCatalog.Collected;
                order.DepositAmountVnd = null;
                order.DeliveredAtUtc = DateTime.UtcNow;
                order.DebtCollectedAtUtc = DateTime.UtcNow;
                break;

            case "UNPAID":
            case "DEPOSIT":
                order.StatusCode = OrderStatusCatalog.DeliveredUnpaid;
                order.PaymentCollectionStatus = key == "DEPOSIT"
                    ? PaymentCollectionCatalog.Deposit
                    : PaymentCollectionCatalog.Uncollected;
                if (depositAmountVnd is > 0)
                    order.DepositAmountVnd = depositAmountVnd;
                order.DeliveredAtUtc = DateTime.UtcNow;
                order.DebtCollectedAtUtc = null;
                break;

            case "FAILED":
                order.StatusCode = OrderStatusCatalog.DeliveryFailed;
                order.PaymentCollectionStatus = PaymentCollectionCatalog.None;
                order.DeliveredAtUtc = DateTime.UtcNow;
                // Giao thất bại → hoàn kho (một lần)
                await _stock.RestoreForOrderAsync(orderId, ct);
                break;

            default:
                return (false, "Kết quả giao không hợp lệ.");
        }

        _timeline.AppendPending(orderId, order.StatusCode,
            note: $"Xác nhận giao: {OrderStatusCatalog.ToDisplayName(order.StatusCode)}.");
        await _db.SaveChangesAsync(ct);

        if (LoyaltyService.IsAwardableStatus(order.StatusCode)
            && !LoyaltyService.IsAwardableStatus(previous))
        {
            var award = await _loyalty.TryAwardForOrderAsync(orderId, ct);
            if (award.Awarded)
            {
                return (true,
                    $"Đã xác nhận giao đơn #{orderId}. Cộng {award.PointsAdded} điểm loyalty.");
            }
        }

        return (true, $"Đã xác nhận giao đơn #{orderId}: {OrderStatusCatalog.ToDisplayName(order.StatusCode)}.");
    }

    public async Task<(bool Ok, string Message)> CollectDebtAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn.");

        if (!OrderStatusCatalog.IsDebtOutstanding(order.StatusCode, order.PaymentCollectionStatus))
            return (false, "Đơn này không còn công nợ.");

        order.StatusCode = OrderStatusCatalog.DeliveredCollected;
        order.PaymentCollectionStatus = PaymentCollectionCatalog.Collected;
        order.DebtCollectedAtUtc = DateTime.UtcNow;
        _timeline.AppendPending(orderId, OrderStatusCatalog.DeliveredCollected, note: "Đã thu công nợ.");
        await _db.SaveChangesAsync(ct);

        var award = await _loyalty.TryAwardForOrderAsync(orderId, ct);
        if (award.Awarded)
            return (true, $"Đã thu công nợ đơn #{orderId}. Cộng {award.PointsAdded} điểm loyalty.");

        return (true, $"Đã thu công nợ đơn #{orderId}.");
    }
}
