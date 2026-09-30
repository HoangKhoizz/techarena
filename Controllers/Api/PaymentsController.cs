using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Domain.Payments;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly PcStoreDbContext _db;
    private readonly IPaymentGatewayService _payments;
    private readonly LoyaltyService _loyalty;

    public PaymentsController(PcStoreDbContext db, IPaymentGatewayService payments, LoyaltyService loyalty)
    {
        _db = db;
        _payments = payments;
        _loyalty = loyalty;
    }

    /// <summary>Tạo link / payUrl MoMo cho đơn đã có (frontend vẽ QR).</summary>
    [HttpPost("momo/create")]
    public async Task<IActionResult> CreateMoMo([FromBody] CreateMoMoPaymentRequest req, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == req.OrderId, ct);
        if (order is null)
            return NotFound(new { error = "Không tìm thấy đơn hàng." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _payments.CreatePaymentUrlAsync(
            order.OrderId,
            order.TotalPriceVnd,
            $"Thanh toan don hang #{order.OrderId}",
            baseUrl,
            ct);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.PayUrl))
            return BadRequest(new { error = result.Error ?? "Không tạo được link thanh toán." });

        order.PaymentMethodCode = "MOMO";
        if (string.IsNullOrWhiteSpace(order.StatusCode) ||
            OrderStatusCatalog.Normalize(order.StatusCode) == OrderStatusCatalog.PendingConfirmation)
            order.StatusCode = OrderStatusCatalog.WaitingPayment;

        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            payUrl = result.PayUrl,
            qrCodeUrl = result.QrCodeUrl,
            requestId = result.RequestId,
            isDemo = result.IsDemo
        });
    }

    /// <summary>Poll trạng thái thanh toán (QR modal).</summary>
    [HttpGet("{orderId:int}/status")]
    public async Task<IActionResult> PaymentStatus(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .Select(x => new { x.OrderId, x.StatusCode, x.PaymentMethodCode, x.TransactionId, x.TotalPriceVnd })
            .SingleOrDefaultAsync(ct);

        if (order is null)
            return NotFound(new { error = "Không tìm thấy đơn." });

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        return Ok(new
        {
            orderId = order.OrderId,
            statusCode = status,
            statusDisplay = OrderStatusCatalog.ToDisplayName(order.StatusCode),
            paymentMethodCode = order.PaymentMethodCode,
            transactionId = order.TransactionId,
            totalPriceVnd = order.TotalPriceVnd,
            paid = status is OrderStatusCatalog.Paid or OrderStatusCatalog.PaidCompleted
        });
    }

    /// <summary>Demo: xác nhận đã quét / thanh toán (chỉ khi UseDemoMode).</summary>
    [HttpPost("momo/demo-confirm/{orderId:int}")]
    public async Task<IActionResult> DemoConfirm(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null)
            return NotFound(new { error = "Không tìm thấy đơn." });

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status is OrderStatusCatalog.Paid or OrderStatusCatalog.PaidCompleted)
            return Ok(new { paid = true, statusCode = status, message = "Đơn đã thanh toán." });

        if (status != OrderStatusCatalog.WaitingPayment)
            return BadRequest(new { error = "Đơn không ở trạng thái chờ thanh toán." });

        await MarkPaidAsync(orderId, $"DEMO{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", ct);
        return Ok(new { paid = true, statusCode = OrderStatusCatalog.Paid, message = "Thanh toán demo thành công." });
    }

    /// <summary>ReturnUrl — khách quay lại sau khi thanh toán MoMo.</summary>
    [HttpGet("momo/return")]
    public async Task<IActionResult> MoMoReturn(
        [FromQuery] string? orderId,
        [FromQuery] string? resultCode,
        [FromQuery] string? message,
        [FromQuery] string? transId,
        [FromQuery] string? extraData,
        [FromQuery] string? amount,
        [FromQuery] string? requestId,
        [FromQuery] string? signature,
        CancellationToken ct)
    {
        var ok = string.Equals(resultCode, "0", StringComparison.Ordinal);
        var localOrderId = ResolveLocalOrderId(orderId, extraData);
        if (localOrderId is null)
            return Redirect($"/Tra-cuu-don-hang?error=invalid");

        if (ok)
            await MarkPaidAsync(localOrderId.Value, transId, ct);

        return Redirect($"/Checkout/Success/{localOrderId.Value}?paid={(ok ? "1" : "0")}");
    }

    /// <summary>IPN / Webhook MoMo.</summary>
    [HttpPost("momo/ipn")]
    public async Task<IActionResult> MoMoIpn(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        Dictionary<string, string> fields;
        try
        {
            fields = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body)
                     ?? new Dictionary<string, string>();
        }
        catch
        {
            fields = Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString());
        }

        if (!_payments.VerifyIpnSignature(fields))
            return BadRequest(new { message = "Invalid signature" });

        fields.TryGetValue("resultCode", out var resultCode);
        fields.TryGetValue("orderId", out var orderId);
        fields.TryGetValue("transId", out var transId);
        fields.TryGetValue("extraData", out var extraData);

        if (string.Equals(resultCode, "0", StringComparison.Ordinal))
        {
            var localId = ResolveLocalOrderId(orderId, extraData);
            if (localId is not null)
                await MarkPaidAsync(localId.Value, transId, ct);
        }

        return Ok(new { message = "Received" });
    }

    private async Task MarkPaidAsync(int orderId, string? transactionId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return;

        var current = OrderStatusCatalog.Normalize(order.StatusCode);
        if (current is OrderStatusCatalog.Paid or OrderStatusCatalog.PaidCompleted or OrderStatusCatalog.Refunded)
            return;

        order.StatusCode = OrderStatusCatalog.Paid;
        order.PaymentMethodCode = "MOMO";
        if (!string.IsNullOrWhiteSpace(transactionId))
            order.TransactionId = transactionId.Trim();

        await _db.SaveChangesAsync(ct);
        await _loyalty.TryAwardForOrderAsync(orderId, ct);
    }

    private static int? ResolveLocalOrderId(string? momoOrderId, string? extraData)
    {
        if (int.TryParse(extraData, out var fromExtra))
            return fromExtra;

        if (string.IsNullOrWhiteSpace(momoOrderId))
            return null;

        var part = momoOrderId.Split('-', 2)[0];
        if (part.StartsWith("TA", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(part[2..], out var id))
            return id;

        return int.TryParse(momoOrderId, out var plain) ? plain : null;
    }
}

public sealed record CreateMoMoPaymentRequest(int OrderId);
