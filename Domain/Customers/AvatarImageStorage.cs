namespace ban_link_kien_PC.Domain.Customers;

/// <summary>Lưu avatar khách hàng vào wwwroot/images/avatars.</summary>
public static class AvatarImageStorage
{
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    public const long MaxBytes = 3 * 1024 * 1024; // 3 MB
    public const string DefaultAvatar = "/images/avatars/default-avatar.svg";

    public static async Task<(bool Ok, string? RelativeUrl, string? Error)> SaveAsync(
        IWebHostEnvironment env,
        IFormFile file,
        int customerId,
        CancellationToken ct = default)
    {
        if (file.Length <= 0)
            return (false, null, "File ảnh trống.");
        if (file.Length > MaxBytes)
            return (false, null, "Ảnh tối đa 3 MB.");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExt.Contains(ext))
            return (false, null, "Chỉ chấp nhận ảnh .jpg, .jpeg, .png, .webp, .gif.");

        var fileName = $"u{customerId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var relativeDir = Path.Combine("images", "avatars");
        var absoluteDir = Path.Combine(env.WebRootPath, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var absolutePath = Path.Combine(absoluteDir, fileName);
        await using (var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream, ct);
        }

        var relativeUrl = "/" + Path.Combine(relativeDir, fileName).Replace('\\', '/');
        return (true, relativeUrl, null);
    }

    public static void TryDelete(IWebHostEnvironment env, string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return;
        if (!relativeUrl.StartsWith("/images/avatars/", StringComparison.OrdinalIgnoreCase))
            return;
        if (relativeUrl.Contains("default-avatar", StringComparison.OrdinalIgnoreCase))
            return;

        var absolute = Path.Combine(env.WebRootPath, relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absolute))
        {
            try { File.Delete(absolute); }
            catch { /* ignore */ }
        }
    }

    public static string DisplayUrl(string? avatarUrl) =>
        string.IsNullOrWhiteSpace(avatarUrl) ? DefaultAvatar : avatarUrl.Trim();
}
