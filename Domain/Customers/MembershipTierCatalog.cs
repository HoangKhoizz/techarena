namespace ban_link_kien_PC.Domain.Customers;

/// <summary>
/// Phân hạng khách thân thiết theo điểm tích lũy.
/// Ngưỡng mặc định (có thể chỉnh sau): Silver ≥ 100, Gold ≥ 500, Platinum ≥ 1500.
/// </summary>
public static class MembershipTierCatalog
{
    public const string None = "NONE";
    public const string Silver = "SILVER";
    public const string Gold = "GOLD";
    public const string Platinum = "PLATINUM";

    public const int SilverMinPoints = 100;
    public const int GoldMinPoints = 500;
    public const int PlatinumMinPoints = 1500;

    public static string Normalize(string? tier)
    {
        var t = (tier ?? string.Empty).Trim().ToUpperInvariant();
        return t switch
        {
            Silver or Gold or Platinum => t,
            _ => None
        };
    }

    public static string ToDisplayName(string? tier) => Normalize(tier) switch
    {
        Silver => "Thẻ Bạc",
        Gold => "Thẻ Vàng",
        Platinum => "Thẻ Bạch kim",
        _ => "Khách thường"
    };

    public static string ResolveFromPoints(int loyaltyPoints)
    {
        if (loyaltyPoints >= PlatinumMinPoints) return Platinum;
        if (loyaltyPoints >= GoldMinPoints) return Gold;
        if (loyaltyPoints >= SilverMinPoints) return Silver;
        return None;
    }

    /// <summary>Ưu đãi giảm giá theo hạng (hiển thị / Phase 3 áp promo).</summary>
    public static decimal DiscountPercent(string? tier) => Normalize(tier) switch
    {
        Silver => 2m,
        Gold => 5m,
        Platinum => 8m,
        _ => 0m
    };

    public static string BenefitSummary(string? tier)
    {
        var pct = DiscountPercent(tier);
        if (pct <= 0) return "Đăng ký thành viên và mua hàng để tích điểm lên hạng.";
        return $"Ưu đãi hạng: giảm {pct:0.#}% trên đơn hàng (áp dụng khi thanh toán).";
    }

    public static int PointsToNextTier(int loyaltyPoints)
    {
        if (loyaltyPoints >= PlatinumMinPoints) return 0;
        if (loyaltyPoints >= GoldMinPoints) return PlatinumMinPoints - loyaltyPoints;
        if (loyaltyPoints >= SilverMinPoints) return GoldMinPoints - loyaltyPoints;
        return SilverMinPoints - loyaltyPoints;
    }

    public static string NextTierName(int loyaltyPoints)
    {
        if (loyaltyPoints >= PlatinumMinPoints) return "";
        if (loyaltyPoints >= GoldMinPoints) return ToDisplayName(Platinum);
        if (loyaltyPoints >= SilverMinPoints) return ToDisplayName(Gold);
        return ToDisplayName(Silver);
    }
}
