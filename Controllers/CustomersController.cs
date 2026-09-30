using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

/// <summary>
/// Quản lý khách hàng e-commerce — chỉ bảng Customer (không lẫn Staff/Admin).
/// </summary>
[Authorize]
public sealed class CustomersController : Controller
{
    private readonly PcStoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly LoyaltyService _loyalty;
    private const int PageSize = 20;
    public const string DefaultResetPassword = "TechArena@123";

    public CustomersController(PcStoreDbContext db, IPasswordHasher hasher, LoyaltyService loyalty)
    {
        _db = db;
        _hasher = hasher;
        _loyalty = loyalty;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, int page = 1, CancellationToken ct = default)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        ViewData["Title"] = "Quản lý khách hàng";
        if (page < 1) page = 1;

        var query = _db.Customers.AsNoTracking()
            .Where(x => x.Username != "admin");
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(x =>
                (x.FullName != null && x.FullName.Contains(key)) ||
                (x.Phone != null && x.Phone.Contains(key)) ||
                x.Email.Contains(key) ||
                x.Username.Contains(key));
        }

        var total = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        if (page > totalPages) page = totalPages;

        var customers = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(x => new CustomerListRowViewModel
            {
                CustomerId = x.CustomerId,
                FullName = x.FullName ?? x.Username,
                Username = x.Username,
                Email = x.Email,
                Phone = x.Phone,
                LoyaltyPoints = x.LoyaltyPoints,
                MembershipTier = x.MembershipTier,
                IsActive = x.IsActive,
                CreatedAtUtc = x.CreatedAtUtc,
                OrderCount = _db.Orders.Count(o => o.CustomerId == x.CustomerId)
            })
            .ToListAsync(ct);

        foreach (var row in customers)
            row.MembershipTierDisplay = MembershipTierCatalog.ToDisplayName(row.MembershipTier);

        return View(new CustomerIndexViewModel
        {
            Keyword = keyword?.Trim(),
            Page = page,
            PageSize = PageSize,
            TotalCount = total,
            TotalPages = totalPages,
            Rows = customers
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;
        ViewData["Title"] = "Thêm khách hàng";
        return View(new CustomerEditViewModel { IsActive = true, Password = "TechArena@123" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerEditViewModel vm, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        NormalizeEdit(vm);
        if (string.IsNullOrWhiteSpace(vm.Username) || string.IsNullOrWhiteSpace(vm.Email) || string.IsNullOrWhiteSpace(vm.Password))
            ModelState.AddModelError("", "Username, Email và Mật khẩu là bắt buộc.");
        if (!string.IsNullOrWhiteSpace(vm.Password) && vm.Password.Length < 6)
            ModelState.AddModelError(nameof(vm.Password), "Mật khẩu tối thiểu 6 ký tự.");

        if (ModelState.IsValid)
        {
            if (await _db.Customers.AnyAsync(x => x.Username == vm.Username, ct))
                ModelState.AddModelError(nameof(vm.Username), "Username đã tồn tại.");
            if (await _db.Customers.AnyAsync(x => x.Email == vm.Email, ct))
                ModelState.AddModelError(nameof(vm.Email), "Email đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Thêm khách hàng";
            return View(vm);
        }

        var (hash, salt) = _hasher.Hash(vm.Password!);
        var entity = MapToEntity(vm, new CustomerEntity
        {
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAtUtc = DateTime.UtcNow
        });
        _loyalty.RefreshTier(entity);
        _db.Customers.Add(entity);
        await _db.SaveChangesAsync(ct);

        TempData["AdminInfo"] = $"Đã tạo khách hàng '{entity.Username}'.";
        return RedirectToAction(nameof(Details), new { id = entity.CustomerId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var c = await _db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == id, ct);
        if (c is null) return NotFound();

        ViewData["Title"] = $"Sửa · {c.FullName ?? c.Username}";
        return View(MapToEditVm(c));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CustomerEditViewModel vm, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        NormalizeEdit(vm);
        if (string.IsNullOrWhiteSpace(vm.Username) || string.IsNullOrWhiteSpace(vm.Email))
            ModelState.AddModelError("", "Username và Email là bắt buộc.");

        if (ModelState.IsValid)
        {
            if (await _db.Customers.AnyAsync(x => x.CustomerId != vm.CustomerId && x.Username == vm.Username, ct))
                ModelState.AddModelError(nameof(vm.Username), "Username đã tồn tại.");
            if (await _db.Customers.AnyAsync(x => x.CustomerId != vm.CustomerId && x.Email == vm.Email, ct))
                ModelState.AddModelError(nameof(vm.Email), "Email đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Sửa khách hàng";
            return View(vm);
        }

        var entity = await _db.Customers.SingleOrDefaultAsync(x => x.CustomerId == vm.CustomerId, ct);
        if (entity is null) return NotFound();

        MapToEntity(vm, entity);
        if (!string.IsNullOrWhiteSpace(vm.Password))
        {
            if (vm.Password.Length < 6)
            {
                ModelState.AddModelError(nameof(vm.Password), "Mật khẩu tối thiểu 6 ký tự.");
                return View(vm);
            }
            var (hash, salt) = _hasher.Hash(vm.Password);
            entity.PasswordHash = hash;
            entity.PasswordSalt = salt;
        }

        _loyalty.RefreshTier(entity);
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = "Đã cập nhật thông tin khách hàng.";
        return RedirectToAction(nameof(Details), new { id = entity.CustomerId });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var customer = await _db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CustomerId == id, ct);
        if (customer is null) return NotFound();

        ViewData["Title"] = $"Khách hàng · {customer.FullName ?? customer.Username}";

        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.CustomerId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new CustomerOrderRowViewModel
            {
                OrderId = x.OrderId,
                CreatedAtUtc = x.CreatedAtUtc,
                OrderType = x.OrderType,
                TotalPriceVnd = x.TotalPriceVnd,
                StatusCode = x.StatusCode,
                PaymentMethodCode = x.PaymentMethodCode
            })
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            var phone = customer.Phone;
            var extra = await _db.Orders.AsNoTracking()
                .Where(x => x.CustomerId == null && x.ReceiverPhone == phone)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new CustomerOrderRowViewModel
                {
                    OrderId = x.OrderId,
                    CreatedAtUtc = x.CreatedAtUtc,
                    OrderType = x.OrderType,
                    TotalPriceVnd = x.TotalPriceVnd,
                    StatusCode = x.StatusCode,
                    PaymentMethodCode = x.PaymentMethodCode
                })
                .ToListAsync(ct);

            var known = orders.Select(o => o.OrderId).ToHashSet();
            foreach (var o in extra)
            {
                if (known.Add(o.OrderId))
                    orders.Add(o);
            }

            orders = orders.OrderByDescending(x => x.CreatedAtUtc).ToList();
        }

        foreach (var o in orders)
        {
            o.StatusDisplay = OrderStatusCatalog.ToDisplayName(o.StatusCode);
            o.StatusBadgeClass = OrderStatusCatalog.ToBadgeClass(o.StatusCode);
            o.OrderTypeDisplay = string.Equals(o.OrderType, "POS", StringComparison.OrdinalIgnoreCase) ? "POS" : "Online";
        }

        return View(new CustomerDetailsViewModel
        {
            CustomerId = customer.CustomerId,
            FullName = customer.FullName ?? customer.Username,
            Username = customer.Username,
            Email = customer.Email,
            Phone = customer.Phone,
            DateOfBirth = customer.DateOfBirth,
            ShippingAddress1 = customer.ShippingAddress1,
            ShippingAddress2 = customer.ShippingAddress2,
            BankAccountInfo = customer.BankAccountInfo,
            LoyaltyPoints = customer.LoyaltyPoints,
            MembershipTier = customer.MembershipTier,
            MembershipTierDisplay = MembershipTierCatalog.ToDisplayName(customer.MembershipTier),
            IsActive = customer.IsActive,
            CreatedAtUtc = customer.CreatedAtUtc,
            Orders = orders
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var customer = await _db.Customers.SingleOrDefaultAsync(x => x.CustomerId == id, ct);
        if (customer is null) return NotFound();

        var (hash, salt) = _hasher.Hash(DefaultResetPassword);
        customer.PasswordHash = hash;
        customer.PasswordSalt = salt;
        customer.PasswordResetToken = null;
        customer.PasswordResetExpiresAtUtc = null;
        await _db.SaveChangesAsync(ct);

        TempData["AdminInfo"] =
            $"Đã reset mật khẩu cho '{customer.Username}'. Mật khẩu mới: {DefaultResetPassword} (gửi cho khách hàng).";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var customer = await _db.Customers.SingleOrDefaultAsync(x => x.CustomerId == id, ct);
        if (customer is null) return NotFound();

        customer.IsActive = !customer.IsActive;
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = customer.IsActive
            ? $"Đã mở khóa tài khoản '{customer.Username}'."
            : $"Đã khóa tài khoản '{customer.Username}'.";
        return RedirectToAction(nameof(Index));
    }

    private static void NormalizeEdit(CustomerEditViewModel vm)
    {
        vm.Username = vm.Username?.Trim() ?? "";
        vm.Email = (vm.Email ?? "").Trim().ToLowerInvariant();
        vm.FullName = string.IsNullOrWhiteSpace(vm.FullName) ? null : vm.FullName.Trim();
        vm.Phone = string.IsNullOrWhiteSpace(vm.Phone) ? null : new string(vm.Phone.Where(char.IsDigit).ToArray());
        vm.ShippingAddress1 = string.IsNullOrWhiteSpace(vm.ShippingAddress1) ? null : vm.ShippingAddress1.Trim();
        vm.ShippingAddress2 = string.IsNullOrWhiteSpace(vm.ShippingAddress2) ? null : vm.ShippingAddress2.Trim();
        vm.BankAccountInfo = string.IsNullOrWhiteSpace(vm.BankAccountInfo) ? null : vm.BankAccountInfo.Trim();
        vm.LoyaltyPoints = Math.Max(0, vm.LoyaltyPoints);
    }

    private static CustomerEditViewModel MapToEditVm(CustomerEntity c) => new()
    {
        CustomerId = c.CustomerId,
        Username = c.Username,
        Email = c.Email,
        FullName = c.FullName,
        Phone = c.Phone,
        DateOfBirth = c.DateOfBirth,
        ShippingAddress1 = c.ShippingAddress1,
        ShippingAddress2 = c.ShippingAddress2,
        BankAccountInfo = c.BankAccountInfo,
        LoyaltyPoints = c.LoyaltyPoints,
        IsActive = c.IsActive
    };

    private static CustomerEntity MapToEntity(CustomerEditViewModel vm, CustomerEntity entity)
    {
        entity.Username = vm.Username;
        entity.Email = vm.Email;
        entity.FullName = vm.FullName;
        entity.Phone = vm.Phone;
        entity.DateOfBirth = vm.DateOfBirth;
        entity.ShippingAddress1 = vm.ShippingAddress1;
        entity.ShippingAddress2 = vm.ShippingAddress2;
        entity.BankAccountInfo = vm.BankAccountInfo;
        entity.LoyaltyPoints = Math.Max(0, vm.LoyaltyPoints);
        entity.IsActive = vm.IsActive;
        return entity;
    }

    private IActionResult? EnsureAdmin() =>
        AdminPortalAccess.EnsureCanAccess(this, User, nameof(Index), "Customers");
}

public sealed class CustomerIndexViewModel
{
    public string? Keyword { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public List<CustomerListRowViewModel> Rows { get; set; } = [];
}

public sealed class CustomerListRowViewModel
{
    public int CustomerId { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public int LoyaltyPoints { get; set; }
    public string MembershipTier { get; set; } = "NONE";
    public string MembershipTierDisplay { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int OrderCount { get; set; }
}

public sealed class CustomerEditViewModel
{
    public int CustomerId { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Password { get; set; }
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? ShippingAddress1 { get; set; }
    public string? ShippingAddress2 { get; set; }
    public string? BankAccountInfo { get; set; }
    public int LoyaltyPoints { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CustomerDetailsViewModel
{
    public int CustomerId { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? ShippingAddress1 { get; set; }
    public string? ShippingAddress2 { get; set; }
    public string? BankAccountInfo { get; set; }
    public int LoyaltyPoints { get; set; }
    public string MembershipTier { get; set; } = "NONE";
    public string MembershipTierDisplay { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<CustomerOrderRowViewModel> Orders { get; set; } = [];
}

public sealed class CustomerOrderRowViewModel
{
    public int OrderId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string OrderType { get; set; } = "ONLINE";
    public string OrderTypeDisplay { get; set; } = "Online";
    public decimal TotalPriceVnd { get; set; }
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "text-bg-light";
    public string PaymentMethodCode { get; set; } = "";
}
