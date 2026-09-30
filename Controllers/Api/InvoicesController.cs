using System.Security.Claims;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/invoices")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class InvoicesController : ControllerBase
{
    private readonly PosInvoiceService _posInvoices;

    public InvoicesController(PosInvoiceService posInvoices) => _posInvoices = posInvoices;

    /// <summary>
    /// Tạo hóa đơn bán tại quầy (POS).
    /// Invoice ≈ dbo.[Order], InvoiceDetails ≈ dbo.OrderItem.
    /// </summary>
    [HttpPost("create-offline")]
    [RequireRoles("Admin", "Staff")]
    public async Task<IActionResult> CreateOffline(
        [FromBody] CreateOfflineInvoiceRequest request,
        CancellationToken ct)
    {
        var staffIdRaw = User.FindFirstValue("staff_id")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(staffIdRaw, out var staffId))
            return Unauthorized(new { error = "Không xác định được nhân viên từ token." });

        var result = await _posInvoices.CreateOfflineAsync(staffId, request, ct);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error });

        return Ok(new
        {
            orderId = result.OrderId,
            invoiceId = result.OrderId,
            totalPriceVnd = result.TotalPriceVnd,
            changeDue = result.ChangeDue,
            receiverName = result.ReceiverName,
            receiverPhone = result.ReceiverPhone,
            createdAtUtc = result.CreatedAtUtc,
            customerId = result.CustomerId,
            message = "Tạo hóa đơn POS thành công."
        });
    }
}
