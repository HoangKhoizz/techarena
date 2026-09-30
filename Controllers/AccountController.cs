using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ban_link_kien_PC.Controllers;

public sealed class AccountController : Controller
{
    private readonly AuthService _auth;
    private readonly StaffPortalAuthService _staffAuth;
    private readonly PcStoreDbContext _db;
    private readonly OrderCancellationService _cancellation;
    private readonly IWebHostEnvironment _env;

    public AccountController(
        AuthService auth,
        StaffPortalAuthService staffAuth,
        PcStoreDbContext db,
        OrderCancellationService cancellation,
        IWebHostEnvironment env)
    {
        _auth = auth;
        _staffAuth = staffAuth;
        _db = db;
        _cancellation = cancellation;
        _env = env;
    }

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterRequest model, CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Username) ||
            string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError("", "Vui lòng nhập đầy đủ thông tin.");
            return View(model);
        }

        var result = await _auth.RegisterAsync(model, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Đăng ký thất bại.");
            return View(model);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginRequest model, string? returnUrl, CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.UsernameOrEmail) ||
            string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError("", "Vui lòng nhập tài khoản và mật khẩu.");
            return View(model);
        }

        // Ưu tiên đăng nhập nhân viên (RBAC Admin portal)
        var staffLogin = await _staffAuth.TrySignInAsync(model.UsernameOrEmail, model.Password, ct);
        if (staffLogin.Ok)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Admin");
        }

        var result = await _auth.LoginAsync(model, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Sai tài khoản hoặc mật khẩu.");
            return View(model);
        }

        // Customer admin (cũ) → portal Admin
        if (string.Equals(result.Customer?.Username, "admin", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Admin");
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    /// <summary>Bắt đầu OAuth Google / Facebook → redirect sang nhà cung cấp.</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        provider = (provider ?? "").Trim();
        if (!string.Equals(provider, GoogleDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "Google", StringComparison.OrdinalIgnoreCase))
        {
            TempData["LoginError"] = "Nhà cung cấp đăng nhập không hợp lệ.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var scheme = GoogleDefaults.AuthenticationScheme;
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, scheme);
    }

    /// <summary>Callback sau Google/Facebook — tạo/đăng nhập Customer + cookie phiên.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginCallback(
        string? returnUrl = null,
        string? remoteError = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            TempData["LoginError"] = $"Đăng nhập ngoài thất bại: {remoteError}";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        const string externalCookieScheme = "ExternalCookie";
        var result = await HttpContext.AuthenticateAsync(externalCookieScheme);
        if (!result.Succeeded || result.Principal is null)
        {
            TempData["LoginError"] = "Không nhận được thông tin từ Google. Thử lại.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var email = result.Principal.FindFirstValue(ClaimTypes.Email)
                    ?? result.Principal.FindFirstValue("email");
        var name = result.Principal.FindFirstValue(ClaimTypes.Name)
                   ?? result.Principal.FindFirstValue("name");
        var picture = result.Principal.FindFirstValue("picture")
                      ?? result.Principal.FindFirstValue("urn:google:picture");

        var authResult = await _auth.LoginOrRegisterExternalAsync(
            "Google",
            email ?? "",
            name,
            picture,
            ct);

        await HttpContext.SignOutAsync(externalCookieScheme);

        if (!authResult.Succeeded)
        {
            TempData["LoginError"] = authResult.Error ?? "Đăng nhập Google thất bại.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Email))
        {
            ModelState.AddModelError("", "Vui lòng nhập email.");
            return View(model);
        }

        var result = await _auth.StartResetPasswordAsync(model, ct);
        if (!result.Succeeded || result.Customer is null)
        {
            ModelState.AddModelError("", result.Error ?? "Không thể tạo mã khôi phục.");
            return View(model);
        }

        ViewBag.ResetToken = result.Customer.PasswordResetToken;
        return View("ForgotPasswordConfirmation");
    }

    [HttpGet]
    public IActionResult ResetPassword(string token)
    {
        return View(new ResetPasswordRequest(token, ""));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Token) || string.IsNullOrWhiteSpace(model.NewPassword))
        {
            ModelState.AddModelError("", "Thiếu mã khôi phục hoặc mật khẩu mới.");
            return View(model);
        }

        var result = await _auth.ResetPasswordAsync(model, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Không thể đổi mật khẩu.");
            return View(model);
        }

        return RedirectToAction("Login");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _auth.LogoutAsync();
        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var customer = await GetCurrentCustomerAsync(ct);
        if (customer is null) return RedirectToAction(nameof(Login));

        return View(ToProfileVm(customer));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> Profile(ProfileUpdateViewModel vm, CancellationToken ct)
    {
        var customer = await GetCurrentCustomerTrackedAsync(ct);
        if (customer is null) return RedirectToAction(nameof(Login));

        vm.Email = (vm.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(vm.Email))
            ModelState.AddModelError(nameof(vm.Email), "Email là bắt buộc.");
        else if (await _db.Customers.AnyAsync(x => x.CustomerId != customer.CustomerId && x.Email == vm.Email, ct))
            ModelState.AddModelError(nameof(vm.Email), "Email đã được dùng bởi tài khoản khác.");

        if (vm.AvatarUpload is { Length: > 0 })
        {
            var saved = await AvatarImageStorage.SaveAsync(_env, vm.AvatarUpload, customer.CustomerId, ct);
            if (!saved.Ok)
                ModelState.AddModelError(nameof(vm.AvatarUpload), saved.Error ?? "Upload avatar thất bại.");
            else
            {
                AvatarImageStorage.TryDelete(_env, customer.AvatarUrl);
                customer.AvatarUrl = saved.RelativeUrl;
            }
        }

        if (!ModelState.IsValid)
        {
            var display = ToProfileVm(customer);
            display.FullName = vm.FullName;
            display.Email = vm.Email;
            display.Phone = vm.Phone;
            display.DateOfBirth = vm.DateOfBirth;
            display.ShippingAddress1 = vm.ShippingAddress1;
            display.ShippingAddress2 = vm.ShippingAddress2;
            display.BankAccountInfo = vm.BankAccountInfo;
            return View(display);
        }

        customer.FullName = string.IsNullOrWhiteSpace(vm.FullName) ? null : vm.FullName.Trim();
        customer.Email = vm.Email;
        customer.Phone = string.IsNullOrWhiteSpace(vm.Phone)
            ? null
            : new string(vm.Phone.Where(char.IsDigit).ToArray());
        customer.DateOfBirth = vm.DateOfBirth;
        customer.ShippingAddress1 = string.IsNullOrWhiteSpace(vm.ShippingAddress1) ? null : vm.ShippingAddress1.Trim();
        customer.ShippingAddress2 = string.IsNullOrWhiteSpace(vm.ShippingAddress2) ? null : vm.ShippingAddress2.Trim();
        customer.BankAccountInfo = string.IsNullOrWhiteSpace(vm.BankAccountInfo) ? null : vm.BankAccountInfo.Trim();

        await _db.SaveChangesAsync(ct);
        TempData["ProfileInfo"] = "Đã cập nhật thông tin hồ sơ.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordFormViewModel());

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordFormViewModel vm, CancellationToken ct)
    {
        if (!TryGetCustomerId(out var customerId))
            return RedirectToAction(nameof(Login));

        if (string.IsNullOrWhiteSpace(vm.CurrentPassword) || string.IsNullOrWhiteSpace(vm.NewPassword))
        {
            ModelState.AddModelError("", "Vui lòng nhập đầy đủ mật khẩu.");
            return View(vm);
        }

        if (vm.NewPassword != vm.ConfirmPassword)
        {
            ModelState.AddModelError(nameof(vm.ConfirmPassword), "Xác nhận mật khẩu không khớp.");
            return View(vm);
        }

        var result = await _auth.ChangePasswordAsync(
            customerId,
            new ChangePasswordRequest(vm.CurrentPassword, vm.NewPassword),
            ct);

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.Error ?? "Không thể đổi mật khẩu.");
            return View(vm);
        }

        TempData["ProfileInfo"] = "Đã đổi mật khẩu thành công.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Loyalty(CancellationToken ct)
    {
        var customer = await GetCurrentCustomerAsync(ct);
        if (customer is null) return RedirectToAction(nameof(Login));

        var next = MembershipTierCatalog.PointsToNextTier(customer.LoyaltyPoints);
        var nextName = MembershipTierCatalog.NextTierName(customer.LoyaltyPoints);

        return View(new LoyaltyPageViewModel
        {
            LoyaltyPoints = customer.LoyaltyPoints,
            MembershipTier = customer.MembershipTier,
            MembershipTierDisplay = MembershipTierCatalog.ToDisplayName(customer.MembershipTier),
            DiscountPercent = MembershipTierCatalog.DiscountPercent(customer.MembershipTier),
            BenefitSummary = MembershipTierCatalog.BenefitSummary(customer.MembershipTier),
            PointsToNextTier = next,
            NextTierDisplay = nextName,
            SilverMin = MembershipTierCatalog.SilverMinPoints,
            GoldMin = MembershipTierCatalog.GoldMinPoints,
            PlatinumMin = MembershipTierCatalog.PlatinumMinPoints
        });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Orders(DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdRaw, out var customerId))
            return RedirectToAction(nameof(Profile));

        var fromUtc = StartOfLocalDayUtc(fromDate);
        var toUtcExclusive = EndOfLocalDayUtcExclusive(toDate);

        var q = _db.Orders.AsNoTracking().Where(x => x.CustomerId == customerId);
        if (fromUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= fromUtc.Value);
        if (toUtcExclusive.HasValue)
            q = q.Where(x => x.CreatedAtUtc < toUtcExclusive.Value);

        var ordersRaw = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new OrderListItemViewModel
            {
                OrderId = x.OrderId,
                StatusCode = x.StatusCode,
                PaymentMethodCode = x.PaymentMethodCode,
                ReceiverName = x.ReceiverName,
                ReceiverPhone = x.ReceiverPhone,
                TotalPriceVnd = x.TotalPriceVnd,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .Take(200)
            .ToListAsync(ct);

        var orders = ordersRaw.Select(x =>
        {
            x.StatusCode = OrderStatusCatalog.Normalize(x.StatusCode);
            x.StatusDisplay = OrderStatusCatalog.ToDisplayName(x.StatusCode);
            x.StatusBadgeClass = OrderStatusCatalog.ToBadgeClass(x.StatusCode);
            x.CanCancel = OrderStatusCatalog.CanCancel(x.StatusCode);
            x.NeedsRefund = OrderStatusCatalog.NeedsOnlineRefund(x.StatusCode, x.PaymentMethodCode);
            return x;
        }).ToList();

        return View(new AccountOrdersPageViewModel
        {
            FromDate = fromDate?.Date,
            ToDate = toDate?.Date,
            Orders = orders
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(int orderId, DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        if (!TryGetCustomerId(out var customerId))
            return RedirectToAction(nameof(Login));

        var order = await _db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == orderId && x.CustomerId == customerId, ct);
        if (order is null)
        {
            TempData["AccountOrdersError"] = "Không tìm thấy đơn hàng.";
            return RedirectToAction(nameof(Orders), new
            {
                fromDate = fromDate?.ToString("yyyy-MM-dd"),
                toDate = toDate?.ToString("yyyy-MM-dd")
            });
        }

        var result = await _cancellation.CancelAsync(orderId, order.ReceiverPhone, ct);
        TempData[result.Succeeded ? "AccountOrdersInfo" : "AccountOrdersError"] =
            result.Succeeded
                ? (result.NewStatus == OrderStatusCatalog.Refunded
                    ? $"Đã hủy đơn #{orderId} và hoàn tiền."
                    : $"Đã hủy đơn #{orderId}.")
                : (result.Error ?? "Không thể hủy đơn.");

        return RedirectToAction(nameof(Orders), new
        {
            fromDate = fromDate?.ToString("yyyy-MM-dd"),
            toDate = toDate?.ToString("yyyy-MM-dd")
        });
    }

    private static DateTime? StartOfLocalDayUtc(DateTime? localDate)
    {
        if (!localDate.HasValue) return null;
        var tz = ResolveVietnamTimeZone();
        var local = DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private static DateTime? EndOfLocalDayUtcExclusive(DateTime? localDate)
    {
        if (!localDate.HasValue) return null;
        var tz = ResolveVietnamTimeZone();
        var nextLocal = DateTime.SpecifyKind(localDate.Value.Date.AddDays(1), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(nextLocal, tz);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
    }

    private bool TryGetCustomerId(out int customerId)
    {
        customerId = 0;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out customerId);
    }

    private async Task<Infrastructure.Persistence.Entities.CustomerEntity?> GetCurrentCustomerAsync(CancellationToken ct)
    {
        if (!TryGetCustomerId(out var id)) return null;
        return await _db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == id, ct);
    }

    private async Task<Infrastructure.Persistence.Entities.CustomerEntity?> GetCurrentCustomerTrackedAsync(CancellationToken ct)
    {
        if (!TryGetCustomerId(out var id)) return null;
        return await _db.Customers.SingleOrDefaultAsync(x => x.CustomerId == id, ct);
    }

    private static ProfileUpdateViewModel ToProfileVm(Infrastructure.Persistence.Entities.CustomerEntity c) => new()
    {
        Username = c.Username,
        FullName = c.FullName,
        Email = c.Email,
        Phone = c.Phone,
        DateOfBirth = c.DateOfBirth,
        ShippingAddress1 = c.ShippingAddress1,
        ShippingAddress2 = c.ShippingAddress2,
        BankAccountInfo = c.BankAccountInfo,
        AvatarUrl = AvatarImageStorage.DisplayUrl(c.AvatarUrl),
        LoyaltyPoints = c.LoyaltyPoints,
        MembershipTier = c.MembershipTier,
        MembershipTierDisplay = MembershipTierCatalog.ToDisplayName(c.MembershipTier),
        BenefitSummary = MembershipTierCatalog.BenefitSummary(c.MembershipTier),
        DiscountPercent = MembershipTierCatalog.DiscountPercent(c.MembershipTier)
    };
}

public sealed class ProfileUpdateViewModel
{
    public string Username { get; set; } = "";
    public string? FullName { get; set; }
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? ShippingAddress1 { get; set; }
    public string? ShippingAddress2 { get; set; }
    public string? BankAccountInfo { get; set; }
    public string AvatarUrl { get; set; } = "/images/avatars/default-avatar.svg";
    public IFormFile? AvatarUpload { get; set; }
    public int LoyaltyPoints { get; set; }
    public string MembershipTier { get; set; } = "NONE";
    public string MembershipTierDisplay { get; set; } = "";
    public string BenefitSummary { get; set; } = "";
    public decimal DiscountPercent { get; set; }
}

public sealed class ChangePasswordFormViewModel
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
}

public sealed class LoyaltyPageViewModel
{
    public int LoyaltyPoints { get; set; }
    public string MembershipTier { get; set; } = "NONE";
    public string MembershipTierDisplay { get; set; } = "";
    public decimal DiscountPercent { get; set; }
    public string BenefitSummary { get; set; } = "";
    public int PointsToNextTier { get; set; }
    public string NextTierDisplay { get; set; } = "";
    public int SilverMin { get; set; }
    public int GoldMin { get; set; }
    public int PlatinumMin { get; set; }
}

public sealed class OrderListItemViewModel
{
    public int OrderId { get; set; }
    public string StatusCode { get; set; } = "";
    public string PaymentMethodCode { get; set; } = "";
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public decimal TotalPriceVnd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "text-bg-light";
    public bool CanCancel { get; set; }
    public bool NeedsRefund { get; set; }
}

public sealed class AccountOrdersPageViewModel
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public List<OrderListItemViewModel> Orders { get; set; } = [];
}
