namespace ban_link_kien_PC.Domain.Catalog;

/// <summary>Lưu ảnh sản phẩm vào wwwroot/images/products.</summary>
public static class ProductImageStorage
{
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    public const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    public static async Task<(bool Ok, string? RelativeUrl, string? Error)> SaveAsync(
        IWebHostEnvironment env,
        IFormFile file,
        string skuOrId,
        CancellationToken ct = default)
    {
        if (file.Length <= 0)
            return (false, null, "File ảnh trống.");
        if (file.Length > MaxBytes)
            return (false, null, "Ảnh tối đa 5 MB.");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExt.Contains(ext))
            return (false, null, "Chỉ chấp nhận ảnh .jpg, .jpeg, .png, .webp, .gif.");

        var safeSku = new string((skuOrId ?? "product")
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeSku))
            safeSku = "product";

        var fileName = $"{safeSku}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var relativeDir = Path.Combine("images", "products");
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
        if (!relativeUrl.StartsWith("/images/products/", StringComparison.OrdinalIgnoreCase))
            return;

        var absolute = Path.Combine(env.WebRootPath, relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absolute))
        {
            try { File.Delete(absolute); }
            catch { /* ignore */ }
        }
    }
}
