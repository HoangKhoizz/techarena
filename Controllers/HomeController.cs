using System.Diagnostics;
using ban_link_kien_PC.Domain.Catalog;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly PcStoreDbContext _db;

    public HomeController(ILogger<HomeController> logger, PcStoreDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var featuredAllSkus = new[]
        {
            "TTG-PB-I5-14600KF-RTX4060TI", "TTG-PB-I7-14700-RTX4070S",
            "TTG-PB-R7-7800X3D-RTX4070", "TTG-PB-I9-14900K-RTX4080S"
        };
        var featuredGamingSkus = featuredAllSkus;
        var featuredGraphicsSkus = new[]
        {
            "TTG-WS-I7-13700-RTX4070", "TTG-WS-R9-7900-RTX4070TI",
            "TTG-WS-I9-14900-RTX4080S", "TTG-WS-R9-9950X-RTX4090"
        };
        var featuredOfficeSkus = new[]
        {
            "TTG-PB-I5-14400F-RTX4060", "TTG-PB-R5-7600-RTX4060",
            "TTG-WS-I5-12600K-RTX4060", "TTG-WS-R7-7700-RTX4060TI"
        };
        var pcGamingSkus = featuredAllSkus;
        var componentSkus = new[]
        {
            "TTG-CPU-I5-14400F", "TTG-MB-B760M-A-D5",
            "TTG-VGA-RTX4070-12", "TTG-SSD-SN580-1T"
        };

        var allSkus = featuredAllSkus
            .Concat(featuredGraphicsSkus)
            .Concat(featuredOfficeSkus)
            .Concat(pcGamingSkus)
            .Concat(componentSkus)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Chỉ SP sẵn sàng bán: có tồn, hoặc Hot (hiện "Cháy hàng")
        var rows = await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            where c.IsActive && (c.StockQty > 0 || c.IsHot)
                  && (allSkus.Contains(c.Sku) || c.IsHot || c.IsBestSeller)
            select new HomeProductRow(
                c.ComponentId, c.Sku, c.Name, c.PriceVnd, c.StockQty, c.IsHot, c.IsBestSeller, cat.Code, c.ImageUrl)
        ).ToListAsync(ct);

        var bySku = rows.ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);

        HomeProductCardVm? CardOrNull(string sku) =>
            bySku.TryGetValue(sku, out var row) ? ToCard(row) : null;

        HomeProductCardVm CardFallback(string sku) =>
            CardOrNull(sku) ?? new HomeProductCardVm
            {
                Sku = sku,
                Name = sku,
                Slug = SeoSlug.From(sku),
                Price = "Liên hệ",
                Specs = "",
                ImageUrl = CatalogImageResolver.Placeholder,
                BestSeller = false,
                IsHot = false,
                IsSoldOut = true
            };

        IReadOnlyList<HomeProductCardVm> Map(IEnumerable<string> skus) =>
            skus.Select(CardOrNull).Where(x => x is not null).Cast<HomeProductCardVm>().ToList();

        // Hot: ưu tiên hiện item cháy hàng trước
        var hotProducts = rows
            .Where(x => x.IsHot)
            .OrderBy(x => x.StockQty > 0 ? 1 : 0)
            .ThenBy(x => x.Name)
            .Take(8)
            .Select(ToCard)
            .ToList();

        var bestSellers = rows
            .Where(x => x.IsBestSeller && x.StockQty > 0)
            .Take(8)
            .Select(ToCard)
            .ToList();

        if (bestSellers.Count == 0)
            bestSellers = Map(featuredAllSkus).Where(x => !x.IsSoldOut).Take(8).ToList();

        var model = new HomeIndexViewModel
        {
            HeroImageUrl = "/images/hero-pc-3d.png",
            Categories =
            [
                new HomeCategoryCardVm
                {
                    Title = "PC Gaming",
                    Description = "Hiệu năng tối đa cho game thủ — FPS cao, tản nhiệt tối ưu",
                    CategoryCode = "PC_PREBUILT",
                    ImageUrl = CardFallback("TTG-PB-I5-14600KF-RTX4060TI").ImageUrl,
                    IconClass = "bi-controller"
                },
                new HomeCategoryCardVm
                {
                    Title = "PC Đồ họa",
                    Description = "Render, thiết kế 2D/3D — workstation chuyên nghiệp",
                    CategoryCode = "PC_WORKSTATION",
                    ImageUrl = CardFallback("TTG-WS-I7-13700-RTX4070").ImageUrl,
                    IconClass = "bi-palette"
                },
                new HomeCategoryCardVm
                {
                    Title = "PC Văn phòng",
                    Description = "Làm việc hiệu quả, tiết kiệm — ổn định lâu dài",
                    CategoryCode = "PC_PREBUILT",
                    ImageUrl = CardFallback("TTG-PB-I5-14400F-RTX4060").ImageUrl,
                    IconClass = "bi-briefcase"
                }
            ],
            HotProducts = hotProducts,
            BestSellers = bestSellers,
            FeaturedAll = Map(featuredAllSkus),
            FeaturedGaming = Map(featuredGamingSkus),
            FeaturedGraphics = Map(featuredGraphicsSkus),
            FeaturedOffice = Map(featuredOfficeSkus),
            PcGaming = Map(pcGamingSkus),
            Components = Map(componentSkus)
        };

        return View(model);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private static HomeProductCardVm ToCard(HomeProductRow row) => new()
    {
        ComponentId = row.ComponentId,
        Sku = row.Sku,
        Name = ToDisplayName(row.Name),
        Slug = SeoSlug.From(row.Name),
        Price = $"{row.PriceVnd:N0}đ",
        Specs = ExtractSpecs(row.Name),
        ImageUrl = CatalogImageResolver.Resolve(row.ComponentId, row.CategoryCode, row.Sku, row.ImageUrl),
        BestSeller = row.IsBestSeller,
        IsHot = row.IsHot,
        IsSoldOut = row.StockQty <= 0
    };

    private static string ToDisplayName(string fullName)
    {
        var head = fullName.Split('|', StringSplitOptions.TrimEntries).FirstOrDefault() ?? fullName;
        return head.Length > 48 ? head[..45] + "..." : head;
    }

    private static string ExtractSpecs(string fullName)
    {
        var parts = fullName.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length <= 1) return "";
        return string.Join(" / ", parts.Skip(1).Take(2));
    }

    private sealed record HomeProductRow(
        int ComponentId,
        string Sku,
        string Name,
        decimal PriceVnd,
        int StockQty,
        bool IsHot,
        bool IsBestSeller,
        string CategoryCode,
        string? ImageUrl);
}
