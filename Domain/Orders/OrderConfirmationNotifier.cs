using System.Net;
using System.Net.Mail;
using ban_link_kien_PC.Domain.Notifications;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ban_link_kien_PC.Domain.Orders;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "noreply@techarena.local";
    public string FromDisplayName { get; set; } = "Tech Arena";
    public bool UseSsl { get; set; } = true;
}

public sealed record OrderConfirmationResult(
    bool ScreenOk,
    bool InAppPushed,
    bool EmailAttempted,
    bool EmailSent,
    string? EmailTo,
    string? EmailError);

public interface IOrderConfirmationNotifier
{
    Task<OrderConfirmationResult> NotifyOrderPlacedAsync(int orderId, CancellationToken ct = default);
}

/// <summary>
/// Thông báo đặt hàng thành công: màn hình (controller), in-app, email (SMTP tùy chọn).
/// </summary>
public sealed class OrderConfirmationNotifier : IOrderConfirmationNotifier
{
    private readonly PcStoreDbContext _db;
    private readonly IUserNotificationCenter _notifications;
    private readonly SmtpOptions _smtp;
    private readonly ILogger<OrderConfirmationNotifier> _logger;

    public OrderConfirmationNotifier(
        PcStoreDbContext db,
        IUserNotificationCenter notifications,
        IOptions<SmtpOptions> smtp,
        ILogger<OrderConfirmationNotifier> logger)
    {
        _db = db;
        _notifications = notifications;
        _smtp = smtp.Value;
        _logger = logger;
    }

    public async Task<OrderConfirmationResult> NotifyOrderPlacedAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null)
            return new OrderConfirmationResult(false, false, false, false, null, "Không tìm thấy đơn.");

        var inApp = false;
        if (order.CustomerId is int uid and > 0)
        {
            _notifications.Push(
                uid,
                "Đặt hàng thành công",
                $"Đơn #{order.OrderId} đã được ghi nhận — {order.TotalPriceVnd:N0} đ ({OrderStatusCatalog.ToDisplayName(order.StatusCode)}).",
                url: $"/Checkout/Success?id={order.OrderId}");
            inApp = true;
        }

        var emailTo = FirstEmail(order.ReceiverEmail);
        if (string.IsNullOrWhiteSpace(emailTo) && order.CustomerId is int cid)
        {
            emailTo = await _db.Customers.AsNoTracking()
                .Where(x => x.CustomerId == cid)
                .Select(x => x.Email)
                .FirstOrDefaultAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(emailTo))
        {
            _logger.LogInformation(
                "Order #{OrderId} placed — no email on file; on-screen confirmation only.",
                orderId);
            return new OrderConfirmationResult(true, inApp, false, false, null, null);
        }

        if (!_smtp.Enabled || string.IsNullOrWhiteSpace(_smtp.Host))
        {
            _logger.LogInformation(
                "Order #{OrderId} confirmation ready for {Email} (SMTP disabled — demo mode).",
                orderId, emailTo);
            return new OrderConfirmationResult(true, inApp, false, false, emailTo,
                "SMTP chưa bật — xem thông báo trên màn hình.");
        }

        try
        {
            var subject = $"[Tech Arena] Đặt hàng thành công — đơn #{order.OrderId}";
            var body =
                $"Xin chào {order.ReceiverName},\n\n" +
                $"Cảm ơn bạn đã đặt hàng tại Tech Arena.\n\n" +
                $"Mã đơn: #{order.OrderId}\n" +
                $"Trạng thái: {OrderStatusCatalog.ToDisplayName(order.StatusCode)}\n" +
                $"Thanh toán: {order.PaymentMethodCode}\n" +
                $"Tổng tiền: {order.TotalPriceVnd:N0} đ\n" +
                $"Địa chỉ giao: {order.ShippingAddress}\n" +
                $"SĐT: {order.ReceiverPhone}\n\n" +
                $"Theo dõi đơn: dùng mã đơn hoặc SĐT trên trang Tra cứu đơn hàng.\n\n" +
                $"Trân trọng,\nTech Arena";

            using var message = new MailMessage
            {
                From = new MailAddress(
                    string.IsNullOrWhiteSpace(_smtp.From) ? "noreply@techarena.local" : _smtp.From,
                    _smtp.FromDisplayName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            message.To.Add(emailTo);

            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl = _smtp.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            if (!string.IsNullOrWhiteSpace(_smtp.UserName))
                client.Credentials = new NetworkCredential(_smtp.UserName, _smtp.Password);

            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Order confirmation email sent to {Email} for #{OrderId}", emailTo, orderId);
            return new OrderConfirmationResult(true, inApp, true, true, emailTo, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send order confirmation email for #{OrderId}", orderId);
            return new OrderConfirmationResult(true, inApp, true, false, emailTo, ex.Message);
        }
    }

    private static string? FirstEmail(string? email)
    {
        var e = (email ?? "").Trim();
        return string.IsNullOrWhiteSpace(e) ? null : e;
    }
}
