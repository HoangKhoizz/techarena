namespace ban_link_kien_PC.Domain.Shipping;

/// <summary>Đơn vị vận chuyển cho UC11 lập phiếu giao hàng.</summary>
public static class ShippingProviderCatalog
{
    public const string Ghtk = "GHTK";
    public const string ViettelPost = "VIETTEL_POST";
    /// <summary>Giao hỏa tốc — shipper cửa hàng (không gọi API ngoài).</summary>
    public const string ExpressStore = "EXPRESS_STORE";

    public static string Normalize(string? code)
    {
        var n = (code ?? "").Trim().ToUpperInvariant().Replace(' ', '_');
        return n switch
        {
            "GHTK" or "GIAOHANGTIETKIEM" => Ghtk,
            "VIETTEL" or "VIETTELPOST" or "VIETTEL_POST" => ViettelPost,
            "EXPRESS" or "HOA_TOC" or "SHIPPER" or "EXPRESS_STORE" => ExpressStore,
            _ => n
        };
    }

    public static bool IsKnown(string? code)
    {
        var n = Normalize(code);
        return n is Ghtk or ViettelPost or ExpressStore;
    }

    public static bool RequiresExternalApi(string? code)
    {
        var n = Normalize(code);
        return n is Ghtk or ViettelPost;
    }

    public static string ToDisplayName(string? code) => Normalize(code) switch
    {
        Ghtk => "Giao Hàng Tiết Kiệm (GHTK)",
        ViettelPost => "Viettel Post",
        ExpressStore => "Giao hỏa tốc (Shipper cửa hàng)",
        _ => code ?? "—"
    };

    public static string TrackingPrefix(string? code) => Normalize(code) switch
    {
        Ghtk => "GHTK",
        ViettelPost => "VTP",
        ExpressStore => "EXPRESS",
        _ => "SHIP"
    };
}
