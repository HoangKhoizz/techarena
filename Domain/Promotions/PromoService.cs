using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Promotions;

public sealed record PromoPreviewResult(
    bool Succeeded,
    string? Error,
    int? PromoCodeId,
    string? PromoCode,
    decimal SubtotalVnd,
    decimal MembershipDiscountVnd,
    decimal PromoDiscountVnd,
    decimal TotalDiscountVnd,
    decimal PayableVnd,
    string? MembershipTier)
{
    public static PromoPreviewResult Fail(string error, decimal subtotal) =>
        new(false, error, null, null, subtotal, 0, 0, 0, subtotal, null);

    public static PromoPreviewResult Ok(
        int? promoId,
        string? promoCode,
        decimal subtotal,
        decimal membershipDisc,
        decimal promoDisc,
        string? tier) =>
        new(true, null, promoId, promoCode, subtotal, membershipDisc, promoDisc,
            membershipDisc + promoDisc, Math.Max(0, subtotal - membershipDisc - promoDisc), tier);
}

/// <summary>Validate mã KM + tính giảm giá hạng thành viên.</summary>
public sealed class PromoService
{
    private readonly PcStoreDbContext _db;

    public PromoService(PcStoreDbContext db) => _db = db;

    public async Task<PromoPreviewResult> PreviewAsync(
        decimal subtotalVnd,
        string? promoCode,
        int? customerId,
        CancellationToken ct = default)
    {
        if (subtotalVnd <= 0)
            return PromoPreviewResult.Fail("Giỏ hàng trống.", 0);

        string? tier = MembershipTierCatalog.None;
        if (customerId is int cid)
        {
            tier = await _db.Customers.AsNoTracking()
                .Where(x => x.CustomerId == cid && x.IsActive)
                .Select(x => x.MembershipTier)
                .FirstOrDefaultAsync(ct) ?? MembershipTierCatalog.None;
        }

        var membershipDisc = RoundVnd(subtotalVnd * MembershipTierCatalog.DiscountPercent(tier) / 100m);

        if (string.IsNullOrWhiteSpace(promoCode))
            return PromoPreviewResult.Ok(null, null, subtotalVnd, membershipDisc, 0, tier);

        var code = promoCode.Trim().ToUpperInvariant();
        var promo = await _db.PromoCodes
            .FirstOrDefaultAsync(x => x.Code == code, ct);

        if (promo is null || !promo.IsActive)
            return PromoPreviewResult.Fail("Mã khuyến mãi không tồn tại hoặc đã tắt.", subtotalVnd);

        var now = DateTime.UtcNow;
        if (promo.StartsAtUtc is DateTime start && now < start)
            return PromoPreviewResult.Fail("Mã khuyến mãi chưa đến thời gian áp dụng.", subtotalVnd);
        if (promo.ExpiresAtUtc is DateTime end && now > end)
            return PromoPreviewResult.Fail("Mã khuyến mãi đã hết hạn.", subtotalVnd);
        if (promo.MaxUses is int max && promo.UsedCount >= max)
            return PromoPreviewResult.Fail("Mã khuyến mãi đã hết lượt sử dụng.", subtotalVnd);
        if (promo.MinOrderVnd is decimal min && subtotalVnd < min)
            return PromoPreviewResult.Fail($"Đơn tối thiểu {min:N0}₫ để dùng mã này.", subtotalVnd);

        if (!string.IsNullOrWhiteSpace(promo.ApplicableTier))
        {
            var required = MembershipTierCatalog.Normalize(promo.ApplicableTier);
            if (TierRank(tier) < TierRank(required))
            {
                return PromoPreviewResult.Fail(
                    $"Mã chỉ dành cho hạng {MembershipTierCatalog.ToDisplayName(required)} trở lên.",
                    subtotalVnd);
            }
        }

        var promoDisc = ComputePromoDiscount(subtotalVnd, promo);
        // Không để giảm quá subtotal
        var maxAllowed = Math.Max(0, subtotalVnd - membershipDisc);
        if (promoDisc > maxAllowed)
            promoDisc = maxAllowed;

        return PromoPreviewResult.Ok(promo.PromoCodeId, promo.Code, subtotalVnd, membershipDisc, promoDisc, tier);
    }

    /// <summary>Tăng UsedCount sau khi đặt đơn thành công (gọi trong cùng transaction nếu có tracked entity).</summary>
    public async Task IncrementUsageAsync(int promoCodeId, CancellationToken ct = default)
    {
        var promo = await _db.PromoCodes.SingleOrDefaultAsync(x => x.PromoCodeId == promoCodeId, ct);
        if (promo is null) return;
        promo.UsedCount += 1;
    }

    public static decimal ComputePromoDiscount(decimal subtotal, PromoCodeEntity promo)
    {
        var type = (promo.DiscountType ?? "PERCENT").Trim().ToUpperInvariant();
        decimal raw = type switch
        {
            "FIXED" => promo.DiscountValue,
            _ => subtotal * promo.DiscountValue / 100m
        };
        raw = RoundVnd(raw);
        if (raw < 0) raw = 0;
        if (raw > subtotal) raw = subtotal;
        return raw;
    }

    private static decimal RoundVnd(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

    private static int TierRank(string? tier) => MembershipTierCatalog.Normalize(tier) switch
    {
        MembershipTierCatalog.Platinum => 3,
        MembershipTierCatalog.Gold => 2,
        MembershipTierCatalog.Silver => 1,
        _ => 0
    };
}
