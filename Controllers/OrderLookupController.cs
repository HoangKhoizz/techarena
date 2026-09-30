using System.Security.Claims;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

/// <summary>Theo dõi đơn hàng theo mã đơn hoặc SĐT (rubric 2.2).</summary>
[Route("Tra-cuu-don-hang")]
public sealed class OrderLookupController : Controller
{
    private readonly PcStoreDbContext _db;
    private readonly OrderCancellationService _cancellation;

    public OrderLookupController(PcStoreDbContext db, OrderCancellationService cancellation)
    {
        _db = db;
        _cancellation = cancellation;
    }

    [HttpGet("")]
    public IActionResult Index(string? phone, int? orderId, string? error)
    {
        ViewData["Title"] = "Theo dõi đơn hàng";
        return View(new OrderLookupPageViewModel
        {
            Phone = phone,
            OrderId = orderId,
            Error = error
        });
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(OrderLookupPageViewModel form, CancellationToken ct)
    {
        ViewData["Title"] = "Theo dõi đơn hàng";
        var phone = NormalizePhone(form.Phone);
        form.Phone = string.IsNullOrWhiteSpace(phone) ? form.Phone : phone;

        if (form.OrderId is int oid and > 0)
        {
            var order = await _db.Orders.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OrderId == oid, ct);
            if (order is null)
            {
                form.Error = $"Không tìm thấy đơn #{oid}.";
                form.Orders = [];
                return View(form);
            }

            // Có SĐT → kiểm tra khớp rồi vào chi tiết; không có SĐT nhưng là chủ đơn đã login → chi tiết
            if (!string.IsNullOrWhiteSpace(phone))
            {
                if (NormalizePhone(order.ReceiverPhone) != phone)
                {
                    form.Error = "Mã đơn và số điện thoại không khớp.";
                    form.Orders = [];
                    return View(form);
                }

                return RedirectToAction(nameof(Details), new { id = oid, phone });
            }

            if (IsOrderOwner(order.CustomerId))
                return RedirectToAction(nameof(Details), new { id = oid });

            form.Error = "Nhập thêm số điện thoại đặt hàng để xem chi tiết đơn (bảo mật).";
            form.Orders =
            [
                ToRow(order)
            ];
            return View(form);
        }

        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 9)
        {
            form.Error = "Nhập mã đơn hàng và/hoặc số điện thoại hợp lệ (SĐT ít nhất 9 số).";
            form.Orders = [];
            return View(form);
        }

        form.Orders = await LoadOrdersByPhoneAsync(phone, ct);
        if (form.Orders.Count == 0)
            form.Error = "Không tìm thấy đơn hàng nào với số điện thoại này.";
        return View(form);
    }

    [HttpGet("chi-tiet/{id:int}")]
    public async Task<IActionResult> Details(int id, string? phone, CancellationToken ct)
    {
        ViewData["Title"] = $"Chi tiết đơn #{id}";
        var order = await _db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == id, ct);
        if (order is null)
            return NotFound();

        var digits = NormalizePhone(phone);
        var isOwner = IsOrderOwner(order.CustomerId);
        var phoneOk = !string.IsNullOrWhiteSpace(digits)
                      && NormalizePhone(order.ReceiverPhone) == digits;

        if (!isOwner && !phoneOk)
        {
            TempData["LookupError"] = "Vui lòng nhập đúng SĐT đặt hàng để xem chi tiết.";
            return RedirectToAction(nameof(Index), new { orderId = id, phone });
        }

        var lines = await (from oi in _db.OrderItems.AsNoTracking()
                           join c in _db.Components.AsNoTracking() on oi.ComponentId equals c.ComponentId
                           where oi.OrderId == id
                           select new OrderLookupLineViewModel
                           {
                               Name = c.Name,
                               Sku = c.Sku,
                               Qty = oi.Qty,
                               UnitPriceVnd = oi.UnitPriceVnd
                           }).ToListAsync(ct);

        return View(new OrderLookupDetailViewModel
        {
            OrderId = order.OrderId,
            ReceiverName = order.ReceiverName,
            ReceiverPhone = order.ReceiverPhone,
            ShippingAddress = order.ShippingAddress,
            PaymentMethodCode = order.PaymentMethodCode,
            StatusCode = OrderStatusCatalog.Normalize(order.StatusCode),
            StatusDisplay = OrderStatusCatalog.ToDisplayName(order.StatusCode),
            StatusBadgeClass = OrderStatusCatalog.ToBadgeClass(order.StatusCode),
            TotalPriceVnd = order.TotalPriceVnd,
            DiscountAmountVnd = order.DiscountAmountVnd,
            CreatedAtUtc = order.CreatedAtUtc,
            DeliveredAtUtc = order.DeliveredAtUtc,
            OrderType = order.OrderType,
            TransactionId = order.TransactionId,
            RefundTransactionId = order.RefundTransactionId,
            CanCancel = OrderStatusCatalog.CanCancel(order.StatusCode),
            NeedsRefund = OrderStatusCatalog.NeedsOnlineRefund(order.StatusCode, order.PaymentMethodCode),
            Lines = lines,
            LookupPhone = phoneOk ? digits : NormalizePhone(order.ReceiverPhone)
        });
    }

    [HttpPost("huy/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? phone, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == id, ct);
        if (order is null) return NotFound();

        var digits = NormalizePhone(phone);
        var isOwner = IsOrderOwner(order.CustomerId);
        var phoneOk = !string.IsNullOrWhiteSpace(digits)
                      && NormalizePhone(order.ReceiverPhone) == digits;
        if (!isOwner && !phoneOk)
        {
            TempData["LookupError"] = "Không đủ quyền hủy đơn.";
            return RedirectToAction(nameof(Index), new { orderId = id });
        }

        var cancelPhone = phoneOk ? digits : order.ReceiverPhone;
        var result = await _cancellation.CancelAsync(id, cancelPhone, ct);
        if (!result.Succeeded)
        {
            TempData["LookupError"] = result.Error ?? "Không thể hủy đơn.";
            return RedirectToAction(nameof(Details), new { id, phone = cancelPhone });
        }

        TempData["LookupInfo"] = result.NewStatus == OrderStatusCatalog.Refunded
            ? $"Đã hủy đơn #{id} và hoàn tiền thành công."
            : $"Đã hủy đơn #{id}.";
        return RedirectToAction(nameof(Details), new { id, phone = cancelPhone });
    }

    private bool IsOrderOwner(int? customerId)
    {
        if (customerId is null or <= 0) return false;
        if (User.Identity?.IsAuthenticated != true) return false;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var uid)) return false;
        return uid == customerId.Value;
    }

    private async Task<List<OrderLookupRowViewModel>> LoadOrdersByPhoneAsync(string phone, CancellationToken ct)
    {
        var candidates = await _db.Orders.AsNoTracking()
            .Where(x => x.ReceiverPhone.Contains(phone)
                        || (phone.Length >= 9 && x.ReceiverPhone.Contains(phone.Substring(phone.Length - 9))))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        return candidates
            .Where(x =>
            {
                var p = NormalizePhone(x.ReceiverPhone);
                return p == phone || (phone.Length >= 9 && p.EndsWith(phone[^9..]));
            })
            .Take(50)
            .Select(ToRow)
            .ToList();
    }

    private static OrderLookupRowViewModel ToRow(Infrastructure.Persistence.Entities.OrderEntity x)
    {
        var status = OrderStatusCatalog.Normalize(x.StatusCode);
        return new OrderLookupRowViewModel
        {
            OrderId = x.OrderId,
            ReceiverName = x.ReceiverName,
            ReceiverPhone = x.ReceiverPhone,
            CreatedAtUtc = x.CreatedAtUtc,
            TotalPriceVnd = x.TotalPriceVnd,
            StatusCode = status,
            StatusDisplay = OrderStatusCatalog.ToDisplayName(status),
            StatusBadgeClass = OrderStatusCatalog.ToBadgeClass(status),
            OrderType = x.OrderType,
            PaymentMethodCode = x.PaymentMethodCode
        };
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        return new string(phone.Where(char.IsDigit).ToArray());
    }
}

public sealed class OrderLookupPageViewModel
{
    public int? OrderId { get; set; }
    public string? Phone { get; set; }
    public string? Error { get; set; }
    public List<OrderLookupRowViewModel> Orders { get; set; } = [];
}

public sealed class OrderLookupRowViewModel
{
    public int OrderId { get; set; }
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public decimal TotalPriceVnd { get; set; }
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "text-bg-light";
    public string OrderType { get; set; } = "ONLINE";
    public string PaymentMethodCode { get; set; } = "";
}

public sealed class OrderLookupDetailViewModel
{
    public int OrderId { get; set; }
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
    public string PaymentMethodCode { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "text-bg-light";
    public decimal TotalPriceVnd { get; set; }
    public decimal DiscountAmountVnd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public string OrderType { get; set; } = "ONLINE";
    public string? TransactionId { get; set; }
    public string? RefundTransactionId { get; set; }
    public bool CanCancel { get; set; }
    public bool NeedsRefund { get; set; }
    public string? LookupPhone { get; set; }
    public List<OrderLookupLineViewModel> Lines { get; set; } = [];
}

public sealed class OrderLookupLineViewModel
{
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
    public decimal UnitPriceVnd { get; set; }
}
