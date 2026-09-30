using System.Security.Claims;

namespace ban_link_kien_PC.Domain.Auth;

/// <summary>Vai trò vận hành Admin portal (RBAC). ClaimTypes.Role trên cookie/JWT.</summary>
public static class StaffRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Sales = "Sales";
    public const string Warehouse = "Warehouse";
    public const string Technical = "Technical";
    public const string Accounting = "Accounting";

    /// <summary>Legacy — vẫn hỗ trợ JWT/API cũ.</summary>
    public const string LegacyAdmin = "Admin";
    public const string LegacyStaff = "Staff";

    public static readonly string[] PortalRoles =
    [
        SuperAdmin, Sales, Warehouse, Technical, Accounting
    ];

    /// <summary>Role mặc định khi tạo nhân viên mới.</summary>
    public const string DefaultNewStaffRole = Sales;

    public static bool IsPortalRole(string? roleName)
    {
        var n = (roleName ?? "").Trim();
        return PortalRoles.Any(r => string.Equals(r, n, StringComparison.OrdinalIgnoreCase))
               || string.Equals(n, LegacyAdmin, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSuperAdmin(ClaimsPrincipal? user) =>
        user is not null && (
            user.IsInRole(SuperAdmin) ||
            user.IsInRole(LegacyAdmin) ||
            string.Equals(user.Identity?.Name, "admin", StringComparison.OrdinalIgnoreCase));

    public static bool CanAccessAdminPortal(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (IsSuperAdmin(user)) return true;
        return PortalRoles.Any(user.IsInRole);
    }

    public static bool IsInAnyRole(ClaimsPrincipal? user, params string[] roles)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (IsSuperAdmin(user)) return true;
        return roles.Any(user.IsInRole);
    }
}
