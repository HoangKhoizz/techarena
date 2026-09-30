namespace ban_link_kien_PC.Infrastructure.Auth;

/// <summary>
/// Đánh dấu endpoint cần JWT + một trong các Role cho phép.
/// Được RoleAuthorizationMiddleware đọc và kiểm tra.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireRolesAttribute : Attribute
{
    public RequireRolesAttribute(params string[] roles)
    {
        Roles = roles?.Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    public IReadOnlyList<string> Roles { get; }
}
