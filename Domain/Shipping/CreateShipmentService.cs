using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Shipping;

/// <summary>UC11 — lập phiếu giao hàng: gọi API vận chuyển (mock) + cập nhật đơn.</summary>
public sealed class CreateShipmentService
{
    public const string ApiErrorMessage = "Lỗi kết nối API vận chuyển, vui lòng thử lại";

    private readonly PcStoreDbContext _db;
    private readonly IShippingService _shipping;
    private readonly OrderTimelineService _timeline;

    public CreateShipmentService(
        PcStoreDbContext db,
        IShippingService shipping,
        OrderTimelineService timeline)
    {
        _db = db;
        _shipping = shipping;
        _timeline = timeline;
    }

    public async Task<(bool Ok, string Message, string? TrackingNumber, string? ProviderCode)> CreateAsync(
        int orderId,
        string providerCode,
        int? assignedStaffId = null,
        CancellationToken ct = default)
    {
        var provider = ShippingProviderCatalog.Normalize(providerCode);
        if (!ShippingProviderCatalog.IsKnown(provider))
            return (false, "Đơn vị vận chuyển không hợp lệ (GHTK / Viettel Post / Giao hỏa tốc).", null, null);

        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null)
            return (false, "Không tìm thấy đơn hàng.", null, null);

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status is not OrderStatusCatalog.Approved and not OrderStatusCatalog.Packed
            and not OrderStatusCatalog.ReadyToDeliver)
            return (false, "Chỉ tạo vận đơn cho đơn Đã duyệt / Đã đóng gói / Sẵn sàng giao.", null, null);

        if (!string.IsNullOrWhiteSpace(order.TrackingNumber))
            return (false, $"Đơn đã có mã vận đơn: {order.TrackingNumber}.", order.TrackingNumber, order.ShippingProvider);

        if (assignedStaffId is int sid)
        {
            var staffOk = await _db.Staffs.AsNoTracking()
                .AnyAsync(x => x.StaffId == sid && x.IsActive, ct);
            if (!staffOk)
                return (false, "Nhân viên giao không hợp lệ hoặc đã khóa.", null, null);
            order.AssignedStaffId = sid;
        }

        var cod = ComputeCodAmount(order);
        var request = new ShippingCreateRequest
        {
            OrderId = order.OrderId,
            ProviderCode = provider,
            ReceiverName = order.ReceiverName,
            ReceiverPhone = order.ReceiverPhone,
            ShippingAddress = order.ShippingAddress,
            CodAmountVnd = cod
        };

        ShippingCreateResult apiResult;
        try
        {
            apiResult = await _shipping.CreateShipmentAsync(request, ct);
        }
        catch (ShippingApiException)
        {
            return (false, ApiErrorMessage, null, null);
        }
        catch (Exception)
        {
            return (false, ApiErrorMessage, null, null);
        }

        if (!apiResult.Ok || string.IsNullOrWhiteSpace(apiResult.TrackingNumber))
            return (false, apiResult.Error ?? ApiErrorMessage, null, null);

        order.TrackingNumber = apiResult.TrackingNumber.Trim();
        order.ShippingProvider = apiResult.ProviderCode ?? provider;
        order.ShippedAtUtc = DateTime.UtcNow;
        order.StatusCode = OrderStatusCatalog.Shipping;
        _timeline.AppendPending(order.OrderId, OrderStatusCatalog.Shipping,
            note: $"Tạo vận đơn {order.TrackingNumber} ({ShippingProviderCatalog.ToDisplayName(order.ShippingProvider)}).");

        await _db.SaveChangesAsync(ct);

        return (
            true,
            $"Đã tạo vận đơn {order.TrackingNumber} ({ShippingProviderCatalog.ToDisplayName(order.ShippingProvider)}).",
            order.TrackingNumber,
            order.ShippingProvider);
    }

    /// <summary>Đánh dấu đóng gói (APPROVED → PACKED) cho kho.</summary>
    public async Task<(bool Ok, string Message)> MarkPackedAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn hàng.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status != OrderStatusCatalog.Approved)
            return (false, "Chỉ đóng gói đơn đang ở trạng thái Đã duyệt.");

        order.StatusCode = OrderStatusCatalog.Packed;
        _timeline.AppendPending(orderId, OrderStatusCatalog.Packed, note: "Đã đóng gói hàng.");
        await _db.SaveChangesAsync(ct);
        return (true, $"Đã đánh dấu đóng gói đơn #{orderId}.");
    }

    public static decimal ComputeCodAmount(Infrastructure.Persistence.Entities.OrderEntity order)
    {
        var pm = (order.PaymentMethodCode ?? "").Trim().ToUpperInvariant();
        if (pm is "MOMO" or "VNPAY" or "PAID" or "BANK")
            return 0m;

        var total = order.TotalPriceVnd;
        if (order.DepositAmountVnd is > 0)
            total = Math.Max(0, total - order.DepositAmountVnd.Value);
        return total;
    }
}
