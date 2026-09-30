using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/orders")]
public sealed class OrdersApiController : ControllerBase
{
    private readonly PcStoreDbContext _db;
    private readonly OrderCancellationService _cancellation;

    public OrdersApiController(PcStoreDbContext db, OrderCancellationService cancellation)
    {
        _db = db;
        _cancellation = cancellation;
    }

    /// <summary>Tra cứu đơn theo SĐT.</summary>
    [HttpGet("by-phone")]
    public async Task<IActionResult> ByPhone([FromQuery] string? phone, CancellationToken ct)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 9)
            return BadRequest(new { error = "Số điện thoại không hợp lệ." });

        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.ReceiverPhone == digits || x.ReceiverPhone.Contains(digits))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.OrderId,
                x.ReceiverName,
                x.ReceiverPhone,
                x.TotalPriceVnd,
                x.StatusCode,
                statusDisplay = OrderStatusCatalog.ToDisplayName(x.StatusCode),
                x.OrderType,
                x.PaymentMethodCode,
                x.CreatedAtUtc,
                canCancel = OrderStatusCatalog.CanCancel(x.StatusCode)
            })
            .Take(50)
            .ToListAsync(ct);

        return Ok(orders);
    }

    /// <summary>
    /// Hủy đơn. Nếu PAID qua MoMo/VNPay → gọi Refund API rồi đánh dấu REFUNDED.
    /// </summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelOrder(int id, [FromBody] CancelOrderRequest? body, CancellationToken ct)
    {
        var result = await _cancellation.CancelAsync(id, body?.Phone, ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error ?? "Không thể hủy đơn." });

        return Ok(new
        {
            orderId = id,
            statusCode = result.NewStatus,
            message = result.NewStatus == OrderStatusCatalog.Refunded
                ? "Đã hủy đơn và hoàn tiền thành công."
                : "Đã hủy đơn hàng."
        });
    }
}

public sealed record CancelOrderRequest(string? Phone);
