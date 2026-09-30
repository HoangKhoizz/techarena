using System.Security.Claims;

namespace ban_link_kien_PC.Infrastructure;

/// <summary>
/// Resolve cart key for current user context:
/// - Logged in: "user:{CustomerId}"
/// - Guest: "guest:{sessionId}"
/// </summary>
public static class CartKeyResolver
{
    public static string GetOrCreateCartKey(HttpContext http)
    {
        var userKey = GetUserCartKey(http);
        if (!string.IsNullOrEmpty(userKey))
            return userKey;

        var sessionKey = CartCookie.GetOrCreateSessionKey(http);
        return ToGuestCartKey(sessionKey);
    }

    public static string? GetExistingCartKey(HttpContext http)
    {
        var userKey = GetUserCartKey(http);
        if (!string.IsNullOrEmpty(userKey))
            return userKey;

        var sessionKey = CartCookie.GetSessionKey(http);
        if (string.IsNullOrEmpty(sessionKey))
            return null;

        return ToGuestCartKey(sessionKey);
    }

    private static string? GetUserCartKey(HttpContext http)
    {
        if (http.User?.Identity?.IsAuthenticated != true)
            return null;

        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        return $"user:{userId.Trim()}";
    }

    private static string ToGuestCartKey(string sessionKey) => $"guest:{sessionKey}";
}
