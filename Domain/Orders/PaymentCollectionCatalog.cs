namespace ban_link_kien_PC.Domain.Orders;

/// <summary>Trạng thái thu tiền khi giao hàng (COD / ký quỹ / công nợ).</summary>
public static class PaymentCollectionCatalog
{
    public const string None = "NONE";
    /// <summary>Đã thu đủ tiền khi giao.</summary>
    public const string Collected = "COLLECTED";
    /// <summary>Giao thành công nhưng chưa thu (công nợ).</summary>
    public const string Uncollected = "UNCOLLECTED";
    /// <summary>Khách đã ký quỹ — được giao chưa thu đủ.</summary>
    public const string Deposit = "DEPOSIT";

    public static string Normalize(string? code)
    {
        var n = (code ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(n) ? None : n;
    }

    public static string ToDisplayName(string? code) => Normalize(code) switch
    {
        Collected => "Đã thu tiền",
        Uncollected => "Chưa thu tiền",
        Deposit => "Ký quỹ",
        _ => "Chưa xác định"
    };
}
