namespace ban_link_kien_PC.Infrastructure;

/// <summary>Dùng chung cho Build PC (thêm giỏ) và trang Giỏ hàng.</summary>
public static class CartCookie
{
    public const string CookieName = "pc_cart_session";

    public static string? GetSessionKey(HttpContext http)
    {
        if (http.Request.Cookies.TryGetValue(CookieName, out var existing) && !string.IsNullOrWhiteSpace(existing))
            return existing;
        return null;
    }

    public static string GetOrCreateSessionKey(HttpContext http)
    {
        var key = GetSessionKey(http);
        if (!string.IsNullOrEmpty(key))
            return key;

        key = Guid.NewGuid().ToString("N");
        http.Response.Cookies.Append(CookieName, key, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });
        return key;
    }
}
