using System.Security.Claims;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Auth;

/// <summary>Đăng nhập nhân viên vào Cookie scheme (Admin UI) kèm ClaimTypes.Role.</summary>
public sealed class StaffPortalAuthService
{
    private readonly PcStoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IHttpContextAccessor _http;

    public StaffPortalAuthService(PcStoreDbContext db, IPasswordHasher hasher, IHttpContextAccessor http)
    {
        _db = db;
        _hasher = hasher;
        _http = http;
    }

    public async Task<(bool Ok, string? Error, string? RoleName)> TrySignInAsync(
        string usernameOrEmail,
        string password,
        CancellationToken ct = default)
    {
        var key = (usernameOrEmail ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(password))
            return (false, "Thiếu tài khoản hoặc mật khẩu.", null);

        var staff = await _db.Staffs.Include(x => x.Role)
            .SingleOrDefaultAsync(x =>
                x.Username == key || x.Email == key.ToLowerInvariant(), ct);

        if (staff is null || !staff.IsActive)
            return (false, null, null); // không phải staff — fallback customer

        // Sai mật khẩu staff → để AccountController thử đăng nhập Customer (vd. admin/123456)
        if (!_hasher.Verify(password, staff.PasswordHash, staff.PasswordSalt))
            return (false, null, null);

        var roleName = staff.Role?.RoleName ?? StaffRoles.DefaultNewStaffRole;
        // Map legacy
        if (string.Equals(roleName, StaffRoles.LegacyAdmin, StringComparison.OrdinalIgnoreCase))
            roleName = StaffRoles.SuperAdmin;
        if (string.Equals(roleName, StaffRoles.LegacyStaff, StringComparison.OrdinalIgnoreCase))
            roleName = StaffRoles.Sales;

        await SignInCookieAsync(staff.StaffId, staff.Username, staff.Email, staff.FullName, roleName);
        return (true, null, roleName);
    }

    public async Task SignInCookieAsync(
        int staffId,
        string username,
        string email,
        string fullName,
        string roleName)
    {
        if (_http.HttpContext is null) return;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, staffId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Email, email ?? ""),
            new(ClaimTypes.GivenName, fullName ?? username),
            new(ClaimTypes.Role, roleName),
            new("auth_kind", "staff"),
            new("staff_id", staffId.ToString())
        };

        // SuperAdmin cũng giữ claim Legacy Admin để tương thích API/policy cũ nếu cần
        if (string.Equals(roleName, StaffRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            claims.Add(new Claim(ClaimTypes.Role, StaffRoles.LegacyAdmin));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await _http.HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
    }
}

/// <summary>Gate chung cho trang Admin MVC theo RBAC.</summary>
public static class AdminPortalAccess
{
    public static IActionResult? EnsureCanAccess(
        Controller controller,
        ClaimsPrincipal user,
        string? returnUrlAction = null,
        string? returnUrlController = null)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return controller.RedirectToAction("Login", "Account", new
            {
                returnUrl = controller.Url.Action(returnUrlAction ?? "Index", returnUrlController ?? "Admin")
            });
        }

        if (!StaffRoles.CanAccessAdminPortal(user))
            return controller.Forbid();

        return null;
    }
}
