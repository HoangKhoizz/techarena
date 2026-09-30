using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ban_link_kien_PC.Domain.Catalog;

/// <summary>Chuyển tên sản phẩm tiếng Việt thành slug URL (ASCII, gạch ngang).</summary>
public static partial class SeoSlug
{
    public static string From(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "san-pham";

        // Lấy phần tên chính trước dấu | (specs)
        var head = input.Split('|', 2, StringSplitOptions.TrimEntries)[0];
        if (string.IsNullOrWhiteSpace(head))
            head = input.Trim();

        var normalized = head.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (ch is 'đ')
            {
                sb.Append('d');
                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9')
            {
                sb.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '/' or '.' or ',' or '+' or '&')
                sb.Append('-');
        }

        var slug = MultiDash().Replace(sb.ToString(), "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "san-pham" : slug;
    }

    public static string ProductPath(string? name, int componentId) =>
        $"/san-pham/{From(name)}-{componentId}";

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}
