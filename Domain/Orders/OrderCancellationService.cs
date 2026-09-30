using ban_link_kien_PC.Domain.Payments;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Orders;

public sealed record CancelOrderServiceResult(bool Succeeded, string? Error, string? NewStatus = null);

/// <summary>
/// Hủy đơn + hoàn tiền MoMo/VNPay (nếu đã PAID) + cộng lại kho trong 1 DbTransaction.
/// </summary>
public sealed class OrderCancellationService
{
    private readonly PcStoreDbContext _db;
    private readonly IPaymentGatewayService _payments;
    private readonly InventoryStockService _stock;
    private readonly ILogger<OrderCancellationService> _logger;

    public OrderCancellationService(
        PcStoreDbContext db,
        IPaymentGatewayService payments,
        InventoryStockService stock,
        ILogger<OrderCancellationService> logger)
    {
        _db = db;
        _payments = payments;
        _stock = stock;
        _logger = logger;
    }

    public async Task<CancelOrderServiceResult> CancelAsync(
        int orderId,
        string? phoneGuard,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
            if (order is null)
                return new CancelOrderServiceResult(false, "Không tìm thấy đơn hàng.");

            if (!string.IsNullOrWhiteSpace(phoneGuard))
            {
                var digits = new string(phoneGuard.Where(char.IsDigit).ToArray());
                var orderPhone = new string(order.ReceiverPhone.Where(char.IsDigit).ToArray());
                if (!string.Equals(digits, orderPhone, StringComparison.Ordinal))
                    return new CancelOrderServiceResult(false, "SĐT không khớp với đơn hàng.");
            }

            if (!OrderStatusCatalog.CanCancel(order.StatusCode))
            {
                var msg = OrderStatusCatalog.IsShippedOrBeyond(order.StatusCode)
                    ? "Đơn đã giao hàng — không thể hủy."
                    : $"Không thể hủy đơn ở trạng thái '{OrderStatusCatalog.ToDisplayName(order.StatusCode)}'.";
                return new CancelOrderServiceResult(false, msg);
            }

            var previousStatus = OrderStatusCatalog.Normalize(order.StatusCode);

            // Đã thanh toán online + chưa giao → bắt buộc Refund trước
            if (OrderStatusCatalog.NeedsOnlineRefund(order.StatusCode, order.PaymentMethodCode))
            {
                var refund = await _payments.RefundTransactionAsync(
                    order.TransactionId ?? $"MISSING-{order.OrderId}",
                    order.TotalPriceVnd,
                    order.OrderId,
                    $"Hoan tien huy don #{order.OrderId}",
                    ct);

                if (!refund.Succeeded)
                {
                    await tx.RollbackAsync(ct);
                    return new CancelOrderServiceResult(false,
                        refund.Error ?? "Hoàn tiền thất bại — đơn vẫn giữ nguyên trạng thái.");
                }

                order.StatusCode = OrderStatusCatalog.Refunded;
                order.RefundTransactionId = refund.RefundTransId;
                order.RefundedAtUtc = DateTime.UtcNow;
                var logLine =
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm}Z] REFUNDED via {order.PaymentMethodCode}" +
                    $" origin={order.TransactionId} refund={refund.RefundTransId}" +
                    (refund.IsDemo ? " (DEMO)" : "");
                order.Note = string.IsNullOrWhiteSpace(order.Note)
                    ? logLine
                    : $"{order.Note}\n{logLine}";

                _logger.LogInformation("Order {OrderId} refunded: {RefundId}", order.OrderId, refund.RefundTransId);
            }
            else
            {
                order.StatusCode = OrderStatusCatalog.Cancelled;
            }

            // Cộng kho nếu đơn trước đó chưa ở trạng thái hủy (đã trừ khi đặt / POS trigger)
            if (!OrderStatusCatalog.IsCancelled(previousStatus))
                await _stock.RestoreForOrderAsync(orderId, ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new CancelOrderServiceResult(true, null, order.StatusCode);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Cancel order {OrderId} failed", orderId);
            return new CancelOrderServiceResult(false, $"Lỗi hủy đơn: {ex.Message}");
        }
    }
}
