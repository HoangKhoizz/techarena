using System.Security.Claims;
using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Domain.Payments;
using ban_link_kien_PC.Domain.Promotions;
using ban_link_kien_PC.Infrastructure;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

public sealed class CheckoutController : Controller
{
    private readonly CartManager _cart;
    private readonly PcStoreDbContext _db;
    private readonly CheckoutFacade _checkout;
    private readonly IPaymentGatewayService _payments;
    private readonly PromoService _promo;
    private readonly IOrderConfirmationNotifier _orderNotify;

    public CheckoutController(
        CartManager cart,
        PcStoreDbContext db,
        CheckoutFacade checkout,
        IPaymentGatewayService payments,
        PromoService promo,
        IOrderConfirmationNotifier orderNotify)
    {
        _cart = cart;
        _db = db;
        _checkout = checkout;
        _payments = payments;
        _promo = promo;
        _orderNotify = orderNotify;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = await BuildCheckoutPageModelAsync(ct);
        if (model.Lines.Count == 0)
        {
            TempData["CheckoutError"] = "Giỏ hàng đang trống, chưa thể thanh toán.";
            return RedirectToAction("Index", "Cart");
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyPromo(CheckoutFormModel form, CancellationToken ct)
    {
        var model = await BuildCheckoutPageModelAsync(ct, form);
        if (model.Lines.Count == 0)
        {
            TempData["CheckoutError"] = "Giỏ hàng đang trống.";
            return RedirectToAction("Index", "Cart");
        }

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(CheckoutFormModel form, CancellationToken ct)
    {
        var model = await BuildCheckoutPageModelAsync(ct, form);
        if (model.Lines.Count == 0)
        {
            TempData["CheckoutError"] = "Giỏ hàng đang trống, chưa thể tạo đơn.";
            return RedirectToAction("Index", "Cart");
        }

        if (string.IsNullOrWhiteSpace(form.ReceiverName))
            ModelState.AddModelError(nameof(form.ReceiverName), "Vui lòng nhập họ tên người nhận.");
        if (string.IsNullOrWhiteSpace(form.ReceiverPhone))
            ModelState.AddModelError(nameof(form.ReceiverPhone), "Vui lòng nhập số điện thoại.");
        if (string.IsNullOrWhiteSpace(form.ShippingAddress))
            ModelState.AddModelError(nameof(form.ShippingAddress), "Vui lòng nhập địa chỉ giao hàng.");

        if (!string.IsNullOrWhiteSpace(form.PromoCode) && !string.IsNullOrWhiteSpace(model.PromoError))
            ModelState.AddModelError("Form.PromoCode", model.PromoError);

        if (!ModelState.IsValid)
            return View("Index", model);

        var cartKey = CartKeyResolver.GetExistingCartKey(HttpContext);
        if (string.IsNullOrEmpty(cartKey))
        {
            TempData["CheckoutError"] = "Không tìm thấy giỏ hàng.";
            return RedirectToAction("Index", "Cart");
        }

        var customerId = TryGetCustomerId();
        var paymentMethod = string.IsNullOrWhiteSpace(form.PaymentMethodCode)
            ? "COD"
            : form.PaymentMethodCode.Trim().ToUpperInvariant();

        var result = await _checkout.PlaceOrderAsync(
            cartKey,
            customerId,
            new CheckoutRequest(
                form.ReceiverName,
                form.ReceiverPhone,
                form.ReceiverEmail,
                form.ShippingAddress,
                form.Note,
                paymentMethod,
                form.PromoCode),
            ct);

        if (!result.Succeeded || result.OrderId is null)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Không thể tạo đơn hàng.");
            return View("Index", model);
        }

        var notify = await _orderNotify.NotifyOrderPlacedAsync(result.OrderId.Value, ct);
        TempData["OrderNotifyEmail"] = notify.EmailTo;
        TempData["OrderNotifyEmailSent"] = notify.EmailSent ? "1" : "0";
        TempData["OrderNotifyInApp"] = notify.InAppPushed ? "1" : "0";
        if (!string.IsNullOrWhiteSpace(notify.EmailError) && !notify.EmailSent)
            TempData["OrderNotifyHint"] = notify.EmailError;

        if (string.Equals(result.PaymentMethodCode, "MOMO", StringComparison.OrdinalIgnoreCase))
        {
            var order = await _db.Orders.SingleAsync(x => x.OrderId == result.OrderId.Value, ct);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var pay = await _payments.CreatePaymentUrlAsync(
                result.OrderId.Value,
                order.TotalPriceVnd,
                $"Thanh toan don hang #{result.OrderId.Value}",
                baseUrl,
                ct);

            if (!pay.Succeeded || string.IsNullOrWhiteSpace(pay.PayUrl))
            {
                TempData["CheckoutError"] = pay.Error ?? "Không tạo được QR MoMo. Đơn đã lưu — thanh toán lại sau.";
                return RedirectToAction(nameof(Success), new { id = result.OrderId.Value });
            }

            order.StatusCode = OrderStatusCatalog.WaitingPayment;
            await _db.SaveChangesAsync(ct);

            TempData["MoMoPayUrl"] = pay.PayUrl;
            TempData["MoMoIsDemo"] = pay.IsDemo ? "1" : "0";
            return RedirectToAction(nameof(Success), new { id = result.OrderId.Value, awaitPay = 1 });
        }

        return RedirectToAction(nameof(Success), new { id = result.OrderId.Value });
    }

    [HttpGet]
    public async Task<IActionResult> Success(int id, string? paid, int? awaitPay, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == id, ct);
        if (order is null)
            return RedirectToAction("Index", "Home");

        var lines = await (from oi in _db.OrderItems.AsNoTracking()
                           join c in _db.Components.AsNoTracking() on oi.ComponentId equals c.ComponentId
                           where oi.OrderId == id
                           select new CheckoutSuccessLine
                           {
                               Name = c.Name,
                               Sku = c.Sku,
                               Qty = oi.Qty,
                               UnitPriceVnd = oi.UnitPriceVnd,
                               LineTotalVnd = oi.UnitPriceVnd * oi.Qty
                           }).ToListAsync(ct);

        string? promoCode = null;
        if (order.PromoCodeId is int pid)
        {
            promoCode = await _db.PromoCodes.AsNoTracking()
                .Where(x => x.PromoCodeId == pid)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(ct);
        }

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        var showQr = awaitPay == 1
                     && string.Equals(order.PaymentMethodCode, "MOMO", StringComparison.OrdinalIgnoreCase)
                     && status == OrderStatusCatalog.WaitingPayment;

        var payUrl = TempData["MoMoPayUrl"] as string;
        var isDemo = TempData["MoMoIsDemo"] as string == "1";
        if (showQr && string.IsNullOrWhiteSpace(payUrl))
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var pay = await _payments.CreatePaymentUrlAsync(
                order.OrderId,
                order.TotalPriceVnd,
                $"Thanh toan don hang #{order.OrderId}",
                baseUrl,
                ct);
            if (pay.Succeeded)
            {
                payUrl = pay.PayUrl;
                isDemo = pay.IsDemo;
            }
            else
            {
                showQr = false;
            }
        }

        return View(new CheckoutSuccessViewModel
        {
            OrderId = order.OrderId,
            ReceiverName = order.ReceiverName,
            ReceiverPhone = order.ReceiverPhone,
            ShippingAddress = order.ShippingAddress,
            PaymentMethodCode = order.PaymentMethodCode,
            StatusCode = status,
            StatusDisplay = OrderStatusCatalog.ToDisplayName(order.StatusCode),
            SubtotalVnd = order.TotalPriceVnd + order.DiscountAmountVnd,
            DiscountAmountVnd = order.DiscountAmountVnd,
            PromoCode = promoCode,
            TotalPriceVnd = order.TotalPriceVnd,
            CanCancel = OrderStatusCatalog.CanCancel(order.StatusCode),
            JustPaid = paid == "1" || status == OrderStatusCatalog.Paid,
            ShowMoMoQr = showQr && !string.IsNullOrWhiteSpace(payUrl),
            MoMoPayUrl = payUrl,
            MoMoIsDemo = isDemo,
            Lines = lines
        });
    }

    private async Task<CheckoutPageViewModel> BuildCheckoutPageModelAsync(
        CancellationToken ct,
        CheckoutFormModel? formOverride = null)
    {
        var cartKey = CartKeyResolver.GetExistingCartKey(HttpContext);
        var model = new CheckoutPageViewModel
        {
            Form = formOverride ?? new CheckoutFormModel(),
            Lines = [],
            GrandTotalVnd = 0
        };

        if (string.IsNullOrEmpty(cartKey))
            return model;

        var cart = _cart.GetCheckoutCart(cartKey);
        if (cart.Count == 0)
            return model;

        var ids = cart.Keys.ToList();
        var components = await _db.Components.AsNoTracking()
            .Where(x => ids.Contains(x.ComponentId))
            .ToDictionaryAsync(x => x.ComponentId, x => x, ct);

        var skippedOutOfStock = 0;
        foreach (var (componentId, qty) in cart)
        {
            if (!components.TryGetValue(componentId, out var c))
                continue;
            if (!c.IsActive || c.StockQty <= 0 || c.StockQty < qty)
            {
                skippedOutOfStock++;
                continue;
            }

            var lineTotal = c.PriceVnd * qty;
            model.Lines.Add(new CheckoutLineViewModel
            {
                Name = c.Name,
                Sku = c.Sku,
                Qty = qty,
                UnitPriceVnd = c.PriceVnd,
                LineTotalVnd = lineTotal
            });
            model.GrandTotalVnd += lineTotal;
        }
        model.SkippedOutOfStockItems = skippedOutOfStock;

        if (formOverride is null)
            await PrefillCustomerInfoAsync(model.Form, ct);

        var customerId = TryGetCustomerId();
        var preview = await _promo.PreviewAsync(
            model.GrandTotalVnd,
            model.Form.PromoCode,
            customerId,
            ct);

        model.MembershipTierDisplay = MembershipTierCatalog.ToDisplayName(preview.MembershipTier);
        model.MembershipDiscountVnd = preview.MembershipDiscountVnd;
        model.PromoDiscountVnd = preview.PromoDiscountVnd;
        model.TotalDiscountVnd = preview.TotalDiscountVnd;
        model.PayableVnd = preview.PayableVnd;
        model.AppliedPromoCode = preview.PromoCode;

        if (!string.IsNullOrWhiteSpace(model.Form.PromoCode) && !preview.Succeeded)
            model.PromoError = preview.Error;
        else if (preview.Succeeded && preview.PromoDiscountVnd > 0)
            model.PromoSuccess = $"Đã áp mã {preview.PromoCode}: -{preview.PromoDiscountVnd:N0}₫";

        return model;
    }

    private async Task PrefillCustomerInfoAsync(CheckoutFormModel form, CancellationToken ct)
    {
        var customerId = TryGetCustomerId();
        if (customerId is null)
            return;

        var customer = await _db.Customers.AsNoTracking()
            .Where(x => x.CustomerId == customerId.Value)
            .Select(x => new { x.FullName, x.Email, x.Phone, x.ShippingAddress1 })
            .SingleOrDefaultAsync(ct);

        if (customer is null)
            return;

        if (string.IsNullOrWhiteSpace(form.ReceiverName))
            form.ReceiverName = customer.FullName ?? "";
        if (string.IsNullOrWhiteSpace(form.ReceiverEmail))
            form.ReceiverEmail = customer.Email;
        if (string.IsNullOrWhiteSpace(form.ReceiverPhone) && !string.IsNullOrWhiteSpace(customer.Phone))
            form.ReceiverPhone = customer.Phone;
        if (string.IsNullOrWhiteSpace(form.ShippingAddress) && !string.IsNullOrWhiteSpace(customer.ShippingAddress1))
            form.ShippingAddress = customer.ShippingAddress1;
    }

    private int? TryGetCustomerId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}

public sealed class CheckoutFormModel
{
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string? ReceiverEmail { get; set; }
    public string ShippingAddress { get; set; } = "";
    public string? Note { get; set; }
    public string PaymentMethodCode { get; set; } = "COD";
    public string? PromoCode { get; set; }
}

public sealed class CheckoutLineViewModel
{
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
    public decimal UnitPriceVnd { get; set; }
    public decimal LineTotalVnd { get; set; }
}

public sealed class CheckoutPageViewModel
{
    public CheckoutFormModel Form { get; set; } = new();
    public List<CheckoutLineViewModel> Lines { get; set; } = [];
    public decimal GrandTotalVnd { get; set; }
    public decimal MembershipDiscountVnd { get; set; }
    public decimal PromoDiscountVnd { get; set; }
    public decimal TotalDiscountVnd { get; set; }
    public decimal PayableVnd { get; set; }
    public string? AppliedPromoCode { get; set; }
    public string? PromoError { get; set; }
    public string? PromoSuccess { get; set; }
    public string MembershipTierDisplay { get; set; } = "";
    public int SkippedOutOfStockItems { get; set; }
}

public sealed class CheckoutSuccessLine
{
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
    public decimal UnitPriceVnd { get; set; }
    public decimal LineTotalVnd { get; set; }
}

public sealed class CheckoutSuccessViewModel
{
    public int OrderId { get; set; }
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
    public string PaymentMethodCode { get; set; } = "COD";
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public decimal SubtotalVnd { get; set; }
    public decimal DiscountAmountVnd { get; set; }
    public string? PromoCode { get; set; }
    public decimal TotalPriceVnd { get; set; }
    public bool CanCancel { get; set; }
    public bool JustPaid { get; set; }
    public bool ShowMoMoQr { get; set; }
    public string? MoMoPayUrl { get; set; }
    public bool MoMoIsDemo { get; set; }
    public List<CheckoutSuccessLine> Lines { get; set; } = [];
}
