using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ban_link_kien_PC.Infrastructure.Auth;

/// <summary>
/// Middleware phân quyền theo Role (RBAC) cho các endpoint gắn [RequireRoles].
/// </summary>
public sealed class RoleAuthorizationMiddleware
{
    private readonly RequestDelegate _next;

    public RoleAuthorizationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var requireRoles = endpoint?.Metadata.GetMetadata<RequireRolesAttribute>();
        if (requireRoles is null || requireRoles.Roles.Count == 0)
        {
            await _next(context);
            return;
        }

        // Cho phép [AllowAnonymous] ghi đè nếu có
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { error = "Chưa đăng nhập hoặc token không hợp lệ." });
            return;
        }

        var userRoles = context.User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Concat(context.User.FindAll("role").Select(c => c.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowed = requireRoles.Roles.Any(r => userRoles.Contains(r));
        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Bạn không có quyền truy cập chức năng này.",
                requiredRoles = requireRoles.Roles
            });
            return;
        }

        await _next(context);
    }
}

public static class RoleAuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseRoleAuthorization(this IApplicationBuilder app)
        => app.UseMiddleware<RoleAuthorizationMiddleware>();
}
