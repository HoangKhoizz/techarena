using System.Text.RegularExpressions;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Catalog;

public sealed class CatalogFilterContext
{
    public Dictionary<int, int> RamCapacityByComponent { get; init; } = [];
}

public interface ICatalogFilterStrategy
{
    IEnumerable<CatalogProductRow> Apply(
        IEnumerable<CatalogProductRow> source,
        CatalogFilterInput input,
        CatalogFilterContext context);
}

public sealed class CatalogFilterFacade
{
    private readonly PcStoreDbContext _db;
    private readonly IEnumerable<ICatalogFilterStrategy> _strategies;

    public CatalogFilterFacade(PcStoreDbContext db, IEnumerable<ICatalogFilterStrategy> strategies)
    {
        _db = db;
        _strategies = strategies;
    }

    public async Task<CatalogPageViewModel> BuildPageAsync(CatalogFilterInput input, CancellationToken ct = default)
    {
        var baseRaw = await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
            from b in bj.DefaultIfEmpty()
            where c.IsActive && (c.StockQty > 0 || c.IsHot)
            select new
            {
                c.ComponentId,
                c.Sku,
                c.Name,
                c.ImageUrl,
                c.PriceVnd,
                c.StockQty,
                c.IsHot,
                c.IsBestSeller,
                CategoryCode = cat.Code,
                CategoryName = cat.DisplayName,
                BrandName = b != null ? b.Name : null
            }
        ).ToListAsync(ct);

        var baseRows = baseRaw.Select(c => new CatalogProductRow(
            c.ComponentId,
            c.Sku,
            c.Name,
            CatalogImageResolver.Resolve(c.ComponentId, c.CategoryCode, c.Sku, c.ImageUrl),
            c.PriceVnd,
            c.StockQty,
            c.IsHot,
            c.IsBestSeller,
            c.CategoryCode,
            c.CategoryName,
            c.BrandName
        )).ToList();

        var context = new CatalogFilterContext
        {
            RamCapacityByComponent = await _db.RamSpecs.AsNoTracking()
                .ToDictionaryAsync(x => x.ComponentId, x => x.CapacityGb, ct)
        };

        IEnumerable<CatalogProductRow> filtered = baseRows;
        foreach (var strategy in _strategies)
            filtered = strategy.Apply(filtered, input, context);

        return new CatalogPageViewModel
        {
            Filter = input,
            Categories = baseRows
                .Select(x => new CatalogCategoryOption(x.CategoryCode, x.CategoryName))
                .DistinctBy(x => x.Code)
                .OrderBy(x => x.DisplayName)
                .ToList(),
            Brands = baseRows
                .Where(x => !string.IsNullOrWhiteSpace(x.BrandName))
                .Select(x => x.BrandName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            CpuLines = baseRows
                .Select(x => CatalogTextExtractors.ExtractCpuLine(x.Name))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            RamCapacitiesGb = baseRows
                .Select(x => CatalogTextExtractors.ExtractRamCapacityGb(x.Name))
                .Concat(context.RamCapacityByComponent.Values.Select(x => (int?)x))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .OrderBy(x => x)
                .ToList(),
            GpuLines = baseRows
                .Select(x => CatalogTextExtractors.ExtractGpuLine(x.Name))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            StorageValues = baseRows
                .Where(x => x.CategoryCode is "SSD" or "HDD" or "PC_WORKSTATION")
                .Select(x => CatalogTextExtractors.ExtractStorageValue(x.Name))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.EndsWith("TB", StringComparison.OrdinalIgnoreCase) ? -1 : 1)
                .ThenBy(x => x)
                .ToList(),
            Products = filtered
                .OrderByDescending(x => ComputeSearchScore(x, input.Keyword))
                .ThenBy(x => x.CategoryCode)
                .ThenBy(x => x.PriceVnd)
                .Take(300)
                .ToList()
        };
    }

    public async Task<IReadOnlyList<CatalogSuggestItem>> SuggestAsync(
        string? keyword,
        string? categoryCode,
        int take = 8,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Trim().Length < 2)
            return [];

        var k = keyword.Trim();
        var c = string.IsNullOrWhiteSpace(categoryCode) ? null : categoryCode.Trim();

        var rows = await (
            from comp in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on comp.ComponentCategoryId equals cat.ComponentCategoryId
            where comp.IsActive && (comp.StockQty > 0 || comp.IsHot)
            select new
            {
                comp.ComponentId,
                comp.Sku,
                comp.Name,
                comp.ImageUrl,
                comp.PriceVnd,
                cat.Code
            }
        ).ToListAsync(ct);

        var filtered = rows
            .Where(x =>
                (c is null || x.Code.Equals(c, StringComparison.OrdinalIgnoreCase)) &&
                (x.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                 x.Sku.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => ComputeSuggestScore(x.Name, x.Sku, k))
            .ThenBy(x => x.PriceVnd)
            .Take(Math.Clamp(take, 1, 20))
            .Select(x => new CatalogSuggestItem(
                x.ComponentId,
                x.Sku,
                x.Name,
                CatalogImageResolver.Resolve(x.ComponentId, x.Code, x.Sku, x.ImageUrl),
                x.PriceVnd,
                x.Code,
                SeoSlug.From(x.Name)))
            .ToList();

        return filtered;
    }

    public async Task<int?> FindComponentIdBySkuAsync(string sku, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return null;

        var normalized = sku.Trim();
        return await _db.Components.AsNoTracking()
            .Where(x => x.IsActive && x.Sku == normalized)
            .Select(x => (int?)x.ComponentId)
            .FirstOrDefaultAsync(ct);
    }

    private static int ComputeSearchScore(CatalogProductRow row, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return 0;

        var k = keyword.Trim();
        var score = 0;

        if (row.Name.StartsWith(k, StringComparison.OrdinalIgnoreCase)) score += 7;
        if (row.Name.Contains(k, StringComparison.OrdinalIgnoreCase)) score += 5;
        if (row.Sku.StartsWith(k, StringComparison.OrdinalIgnoreCase)) score += 6;
        if (row.Sku.Contains(k, StringComparison.OrdinalIgnoreCase)) score += 4;
        if (!string.IsNullOrWhiteSpace(row.BrandName) && row.BrandName.Contains(k, StringComparison.OrdinalIgnoreCase)) score += 3;
        if (row.CategoryName.Contains(k, StringComparison.OrdinalIgnoreCase)) score += 2;

        return score;
    }

    private static int ComputeSuggestScore(string name, string sku, string keyword)
    {
        var score = 0;
        if (name.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) score += 8;
        if (sku.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) score += 7;
        if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) score += 5;
        if (sku.Contains(keyword, StringComparison.OrdinalIgnoreCase)) score += 4;
        return score;
    }
}

public static class CatalogTextExtractors
{
    public static string? ExtractCpuLine(string name)
    {
        var m1 = Regex.Match(name, @"CORE\s+ULTRA\s+[579]", RegexOptions.IgnoreCase);
        if (m1.Success) return m1.Value.ToUpperInvariant();

        var m2 = Regex.Match(name, @"I[3579]-?\d{4,5}[A-Z]*", RegexOptions.IgnoreCase);
        if (m2.Success) return m2.Value.ToUpperInvariant();

        var m3 = Regex.Match(name, @"RYZEN\s+[3579]\s+\d{4,5}(?:X3D|X|F)?", RegexOptions.IgnoreCase);
        if (m3.Success) return m3.Value.ToUpperInvariant();

        return null;
    }

    public static string? ExtractGpuLine(string name)
    {
        var m = Regex.Match(name, @"(?:RTX|RX)\s?\d{4}(?:\s?TI|\s?SUPER|\s?XT)?", RegexOptions.IgnoreCase);
        return m.Success ? Regex.Replace(m.Value.ToUpperInvariant(), @"\s+", " ").Trim() : null;
    }

    public static int? ExtractRamCapacityGb(string name)
    {
        var m = Regex.Match(name, @"\b(8|16|24|32|48|64|96|128)\s?GB\b", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return int.TryParse(m.Groups[1].Value, out var gb) ? gb : null;
    }

    public static string? ExtractStorageValue(string name)
    {
        var m = Regex.Match(name, @"\b(120|128|240|250|256|480|500|512|960|1000|1024|2000|2048|4000)\s?GB\b|\b(1|2|4|8)\s?TB\b", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return Regex.Replace(m.Value.ToUpperInvariant(), @"\s+", "");
    }
}

public static class CatalogImageResolver
{
    private static readonly string[] CpuImages =
    [
        "https://ttgshop.vn/media/product/1072100340_cpu_intel_core_i5_11400f_tray.png"
    ];

    private static readonly string[] MainboardImages =
    [
        "https://ttgshop.vn/media/product/250_1072100128_13157_13146_12317_dsc08264_web.jpg",
        "https://ttgshop.vn/media/product/250_1072100129_13160_13145_12316_dsc08309_1.jpg"
    ];

    private static readonly string[] RamImages =
    [
        "https://ttgshop.vn/media/product/1072100333_ram_adata_xpg_lancer_blade_white_2x16gb_ddr5_5600mhz_ax5u5600c4616g_dtlabwh.jpg"
    ];

    private static readonly string[] GpuImages =
    [
        "https://ttgshop.vn/media/product/1072100205_11104_card_man_hinh_colorful_rtx_3060_nb_duo_12g_v3_l_v_1.jpg"
    ];

    private static readonly string[] SsdImages =
    [
        "https://ttgshop.vn/media/product/1064369500_wd_green_sn3000_nvme_ssd_front_p__3__174e7be0944a42dcb87baa3e9a18c8e2.png"
    ];

    private static readonly string[] HddImages =
    [
        "https://ttgshop.vn/media/product/1054375528_9192_hdd_western_caviar_black_1tb_7200rpm_sata3_6gbs_64mb_cache_001_70369953038e4392b9a8f9c8ee1be383.jpg"
    ];

    private static readonly string[] PsuImages =
    [
        "https://ttgshop.vn/media/product/250_1072100194_12766_dsc05245_copy.jpg",
        "https://ttgshop.vn/media/product/250_1072100195_12766_dsc05245_copy.jpg"
    ];

    private static readonly string[] CaseImages =
    [
        "https://ttgshop.vn/media/product/250_1072100200_13149_vo_case_jonsbo_tk_1.jpg",
        "https://ttgshop.vn/media/product/250_1072100231_13241_13146_12317_dsc08264_web.jpg"
    ];

    private static readonly Dictionary<string, string> SkuImages = new(StringComparer.OrdinalIgnoreCase)
    {
        // HOME FEATURED - LINH KIEN
        ["TTG-CPU-I5-14400F"] = "https://ttgshop.vn/media/product/1072100340_cpu_intel_core_i5_11400f_tray.png",
        ["TTG-MB-B760M-A-D5"] = "https://ttgshop.vn/media/product/250_1072100128_13157_13146_12317_dsc08264_web.jpg",
        ["TTG-VGA-RTX4070-12"] = "https://ttgshop.vn/media/product/1072100205_11104_card_man_hinh_colorful_rtx_3060_nb_duo_12g_v3_l_v_1.jpg",
        ["TTG-SSD-SN580-1T"] = "https://ttgshop.vn/media/product/1064369500_wd_green_sn3000_nvme_ssd_front_p__3__174e7be0944a42dcb87baa3e9a18c8e2.png",

        // HOME FEATURED - PC PREBUILT
        ["TTG-PB-I5-14600KF-RTX4060TI"] = "https://ttgshop.vn/media/product/250_1072100357_13366_pc_gaming_core_ultra_7_270k_plus_rtx_5070_12gb_oc.jpg",
        ["TTG-PB-I7-14700-RTX4070S"] = "https://ttgshop.vn/media/product/250_1072100357_13366_pc_gaming_core_ultra_7_270k_plus_rtx_5070_12gb_oc.jpg",
        ["TTG-PB-R7-7800X3D-RTX4070"] = "https://ttgshop.vn/media/product/250_1072100359_13366_pc_gaming_core_ultra_7_270k_plus_rtx_5070_12gb_oc2.jpg",
        ["TTG-PB-I9-14900K-RTX4080S"] = "https://ttgshop.vn/media/product/250_1072100277_pcm_dsc03236.jpg",
        ["TTG-PB-I5-14400F-RTX4060"] = "https://ttgshop.vn/media/product/250_1072100124_dsc09857_copy.jpg",
        ["TTG-PB-R5-7600-RTX4060"] = "https://ttgshop.vn/media/product/250_1072100128_13157_13146_12317_dsc08264_web.jpg",
        ["TTG-PB-R9-7900-RTX4070TI"] = "https://ttgshop.vn/media/product/250_1072100126_13146_12317_dsc08252_web.jpg",
        ["TTG-PB-R9-9950X-RTX4090"] = "https://ttgshop.vn/media/product/250_1072100131_13158_12886_11344_dsc05344.jpg",

        // HOME FEATURED - PC WORKSTATION
        ["TTG-WS-I5-12600K-RTX4060"] = "https://ttgshop.vn/media/product/250_1072100200_13149_vo_case_jonsbo_tk_1.jpg",
        ["TTG-WS-I7-13700-RTX4070"] = "https://ttgshop.vn/media/product/250_1072100207_12900_dsc05732.jpg",
        ["TTG-WS-R7-7700-RTX4060TI"] = "https://ttgshop.vn/media/product/250_1072100231_13241_13146_12317_dsc08264_web.jpg",
        ["TTG-WS-I7-14700-RTX4070S"] = "https://ttgshop.vn/media/product/250_1072100198_13163_pcm_dsc02328.jpg",
        ["TTG-WS-R9-7900-RTX4070TI"] = "https://ttgshop.vn/media/product/250_1072100264_13247_pcm_dsc02590.jpg",
        ["TTG-WS-I9-14900-RTX4080S"] = "https://ttgshop.vn/media/product/250_1072100240_pcm_dsc02590.jpg",
        ["TTG-WS-R9-9950X-RTX4090"] = "https://ttgshop.vn/media/product/250_1072100197_11000_11808_dsc03858_copy.jpg",

        // KEYBOARD
        ["TTG-KB-G713"] = "https://ttgshop.vn/media/product/250_1072100207_12900_dsc05732.jpg",
        ["TTG-KB-BWTEV3"] = "https://ttgshop.vn/media/product/250_1072100203_12338_dsc04984.jpg",
        ["TTG-KB-K2-V2"] = "https://ttgshop.vn/media/product/250_1072100207_12900_dsc05732.jpg",

        // MOUSE
        ["TTG-MS-G502X"] = "https://ttgshop.vn/media/product/250_1072100202_pcm_dsc02930.jpg",
        ["TTG-MS-DAV3"] = "https://ttgshop.vn/media/product/250_1072100201_11046_pcm_dsc02359.jpg",
        ["TTG-MS-PULSEFIRE-HST2"] = "https://ttgshop.vn/media/product/250_1072100202_pcm_dsc02930.jpg",

        // HEADSET
        ["TTG-HS-CLOD2"] = "https://ttgshop.vn/media/product/250_1072100198_13163_pcm_dsc02328.jpg",
        ["TTG-HS-BKSHV3"] = "https://ttgshop.vn/media/product/250_1072100197_11000_11808_dsc03858_copy.jpg",

        // ACCESSORY
        ["TTG-ACC-LITEPAD"] = "https://ttgshop.vn/media/product/250_1072100203_12338_dsc04984.jpg",
        ["TTG-ACC-INTHUB"] = "https://ttgshop.vn/media/product/250_1072100202_pcm_dsc02930.jpg",
        ["TTG-ACC-GIGANTUSV2"] = "https://ttgshop.vn/media/product/250_1072100207_12900_dsc05732.jpg",

        // FAN
        ["TTG-FAN-CF120"] = "https://ttgshop.vn/media/product/1072100324_fan_case_lian_li_uni_fan_sl_inf_wireless_120_black_pack_3c_2.jpg",
        ["TTG-FAN-MF120"] = "https://ttgshop.vn/media/product/1072100324_fan_case_lian_li_uni_fan_sl_inf_wireless_120_black_pack_3c_2.jpg",
        ["TTG-FAN-P12-PST"] = "https://ttgshop.vn/media/product/1072100324_fan_case_lian_li_uni_fan_sl_inf_wireless_120_black_pack_3c_2.jpg",

        // COOLER
        ["TTG-AIR-H212"] = "https://ttgshop.vn/media/product/1072100347_tan_nhiet_khi_tryx_turris_620_white_pcm_2.jpg",
        ["TTG-AIR-AG400"] = "https://ttgshop.vn/media/product/1072100347_tan_nhiet_khi_tryx_turris_620_white_pcm_2.jpg",
        ["TTG-AIO-ML240L"] = "https://ttgshop.vn/media/product/1072100347_tan_nhiet_khi_tryx_turris_620_white_pcm_2.jpg",

        // CHAIR
        ["TTG-CHAIR-ISkur"] = "https://ttgshop.vn/media/product/250_1072100201_11046_pcm_dsc02359.jpg"
    };

    private static readonly string[] TtgCommonImages =
    [
        "https://ttgshop.vn/media/product/250_1072100357_13366_pc_gaming_core_ultra_7_270k_plus_rtx_5070_12gb_oc.jpg",
        "https://ttgshop.vn/media/product/250_1072100357_13366_pc_gaming_core_ultra_7_270k_plus_rtx_5070_12gb_oc.jpg",
        "https://ttgshop.vn/media/product/250_1072100359_13366_pc_gaming_core_ultra_7_270k_plus_rtx_5070_12gb_oc2.jpg",
        "https://ttgshop.vn/media/product/250_1072100277_pcm_dsc03236.jpg",
        "https://ttgshop.vn/media/product/250_1072100275_1072100274_pcm_dsc02593.jpg",
        "https://ttgshop.vn/media/product/250_1072100264_13247_pcm_dsc02590.jpg",
        "https://ttgshop.vn/media/product/250_1072100244_pcm_dsc02644.jpg",
        "https://ttgshop.vn/media/product/250_1072100240_pcm_dsc02590.jpg",
        "https://ttgshop.vn/media/product/250_1072100238_pcm_dsc02590.jpg",
        "https://ttgshop.vn/media/product/250_1072100234_pcm_dsc02630.jpg",
        "https://ttgshop.vn/media/product/250_1072100231_13241_13146_12317_dsc08264_web.jpg",
        "https://ttgshop.vn/media/product/250_1072100207_12900_dsc05732.jpg",
        "https://ttgshop.vn/media/product/250_1072100203_12338_dsc04984.jpg",
        "https://ttgshop.vn/media/product/250_1072100202_pcm_dsc02930.jpg",
        "https://ttgshop.vn/media/product/250_1072100201_11046_pcm_dsc02359.jpg",
        "https://ttgshop.vn/media/product/250_1072100200_13149_vo_case_jonsbo_tk_1.jpg",
        "https://ttgshop.vn/media/product/250_1072100198_13163_pcm_dsc02328.jpg",
        "https://ttgshop.vn/media/product/250_1072100197_11000_11808_dsc03858_copy.jpg",
        "https://ttgshop.vn/media/product/250_1072100196_11000_11808_dsc03858_copy.jpg",
        "https://ttgshop.vn/media/product/250_1072100195_12766_dsc05245_copy.jpg",
        "https://ttgshop.vn/media/product/250_1072100194_12766_dsc05245_copy.jpg",
        "https://ttgshop.vn/media/product/250_1072100193_1072100192_11053_11396_dsc03092_4cff8ed202214cd1ae1c805f68049b72_master.jpg",
        "https://ttgshop.vn/media/product/250_1072100192_11053_11396_dsc03092_4cff8ed202214cd1ae1c805f68049b72_master.jpg",
        "https://ttgshop.vn/media/product/250_1072100191_pcm_dsc02344.jpg",
        "https://ttgshop.vn/media/product/250_1072100131_13158_12886_11344_dsc05344.jpg",
        "https://ttgshop.vn/media/product/250_1072100130_13159_13125_12869_11344_12283_11879_pc_mini_i5_14600kf_rtx_4070_12gb_pcm_4__1_.jpg",
        "https://ttgshop.vn/media/product/250_1072100129_13160_13145_12316_dsc08309_1.jpg",
        "https://ttgshop.vn/media/product/250_1072100128_13157_13146_12317_dsc08264_web.jpg",
        "https://ttgshop.vn/media/product/250_1072100126_13146_12317_dsc08252_web.jpg",
        "https://ttgshop.vn/media/product/250_1072100125_pcm_dsc03459.jpg",
        "https://ttgshop.vn/media/product/250_1072100124_dsc09857_copy.jpg",
        "https://ttgshop.vn/media/product/250_1072100123_12295_11982_dsc07069.jpg",
        "https://ttgshop.vn/media/product/250_1072100106_12551_dsc05580__1__ce1faa9c0afa4866b7baf7624469043c.jpg"
    ];

    private static readonly Dictionary<string, string[]> CategoryImages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CPU"] =
            CpuImages,
        ["MAINBOARD"] =
            MainboardImages,
        ["RAM"] =
            RamImages,
        ["GPU"] =
            GpuImages,
        ["SSD"] =
            SsdImages,
        ["HDD"] =
            HddImages,
        ["CASE"] =
            CaseImages,
        ["PSU"] =
            PsuImages,
        ["MONITOR"] =
        [
            "https://ttgshop.vn/media/product/250_1072100277_pcm_dsc03236.jpg"
        ],
        ["KEYBOARD"] =
        [
            "https://ttgshop.vn/media/product/250_1072100207_12900_dsc05732.jpg",
            "https://ttgshop.vn/media/product/250_1072100203_12338_dsc04984.jpg"
        ],
        ["MOUSE"] =
        [
            "https://ttgshop.vn/media/product/250_1072100202_pcm_dsc02930.jpg",
            "https://ttgshop.vn/media/product/250_1072100201_11046_pcm_dsc02359.jpg"
        ],
        ["HEADSET"] =
        [
            "https://ttgshop.vn/media/product/250_1072100198_13163_pcm_dsc02328.jpg",
            "https://ttgshop.vn/media/product/250_1072100197_11000_11808_dsc03858_copy.jpg"
        ],
        ["PC_WORKSTATION"] =
        [
            "https://ttgshop.vn/media/product/250_1072100275_1072100274_pcm_dsc02593.jpg",
            "https://ttgshop.vn/media/product/250_1072100264_13247_pcm_dsc02590.jpg"
        ],
        ["ACCESSORY"] =
        [
            "https://ttgshop.vn/media/product/250_1072100203_12338_dsc04984.jpg",
            "https://ttgshop.vn/media/product/250_1072100202_pcm_dsc02930.jpg",
            "https://ttgshop.vn/media/product/250_1072100207_12900_dsc05732.jpg"
        ],
        ["FAN"] =
        [
            "https://ttgshop.vn/media/product/1072100324_fan_case_lian_li_uni_fan_sl_inf_wireless_120_black_pack_3c_2.jpg"
        ],
        ["COOLER_AIR"] =
        [
            "https://ttgshop.vn/media/product/1072100347_tan_nhiet_khi_tryx_turris_620_white_pcm_2.jpg"
        ],
        ["COOLER_AIO"] =
        [
            "https://ttgshop.vn/media/product/1072100347_tan_nhiet_khi_tryx_turris_620_white_pcm_2.jpg"
        ],
        ["CHAIR"] =
        [
            "https://ttgshop.vn/media/product/250_1072100201_11046_pcm_dsc02359.jpg"
        ]
    };

    public const string Placeholder = "/images/placeholder-product.svg";

    private const string FallbackImage = Placeholder;

    public static string Resolve(int componentId, string categoryCode, string? sku = null, string? customImageUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(customImageUrl))
            return customImageUrl.Trim();

        if (!string.IsNullOrWhiteSpace(sku) && SkuImages.TryGetValue(sku.Trim(), out var imageBySku))
            return imageBySku;

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var s = sku.Trim().ToUpperInvariant();
            if (s.StartsWith("TTG-CPU-")) return PickByKey(s, CpuImages);
            if (s.StartsWith("TTG-MB-")) return PickByKey(s, MainboardImages);
            if (s.StartsWith("TTG-RAM-")) return PickByKey(s, RamImages);
            if (s.StartsWith("TTG-VGA-")) return PickByKey(s, GpuImages);
            if (s.StartsWith("TTG-SSD-")) return PickByKey(s, SsdImages);
            if (s.StartsWith("TTG-HDD-")) return PickByKey(s, HddImages);
            if (s.StartsWith("TTG-PSU-")) return PickByKey(s, PsuImages);
            if (s.StartsWith("TTG-CASE-")) return PickByKey(s, CaseImages);
        }

        if (!CategoryImages.TryGetValue(categoryCode, out var images) || images.Length == 0)
            images = TtgCommonImages;

        var idx = Math.Abs(componentId % images.Length);
        return images[idx] ?? FallbackImage;
    }

    private static string PickByKey(string key, string[] images)
    {
        if (images.Length == 0)
            return FallbackImage;

        var hash = 0;
        foreach (var ch in key)
            hash = ((hash * 31) + ch) & 0x7fffffff;
        return images[hash % images.Length];
    }
}

public sealed class PriceRangeFilterStrategy : ICatalogFilterStrategy
{
    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (input.MinPriceVnd.HasValue)
            source = source.Where(x => x.PriceVnd >= input.MinPriceVnd.Value);
        if (input.MaxPriceVnd.HasValue)
            source = source.Where(x => x.PriceVnd <= input.MaxPriceVnd.Value);
        return source;
    }
}

public sealed class CategoryFilterStrategy : ICatalogFilterStrategy
{
    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (string.IsNullOrWhiteSpace(input.CategoryCode))
            return source;
        var c = input.CategoryCode.Trim();
        return source.Where(x => x.CategoryCode.Equals(c, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class KeywordFilterStrategy : ICatalogFilterStrategy
{
    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (string.IsNullOrWhiteSpace(input.Keyword))
            return source;

        var k = input.Keyword.Trim();
        return source.Where(x =>
            x.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            x.Sku.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(x.BrandName) && x.BrandName.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
            x.CategoryName.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class BrandFilterStrategy : ICatalogFilterStrategy
{
    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (string.IsNullOrWhiteSpace(input.Brand))
            return source;
        return source.Where(x => string.Equals(x.BrandName, input.Brand.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class CpuLineFilterStrategy : ICatalogFilterStrategy
{
    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (string.IsNullOrWhiteSpace(input.CpuLine))
            return source;
        return source.Where(x => x.Name.Contains(input.CpuLine.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class RamCapacityFilterStrategy : ICatalogFilterStrategy
{
    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (!input.RamCapacityGb.HasValue)
            return source;

        var gb = input.RamCapacityGb.Value;
        return source.Where(x =>
            (context.RamCapacityByComponent.TryGetValue(x.ComponentId, out var cap) && cap == gb)
            || Regex.IsMatch(x.Name, $@"\b{gb}\s?GB\b", RegexOptions.IgnoreCase));
    }
}

public sealed class GpuFilterStrategy : ICatalogFilterStrategy
{
    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (string.IsNullOrWhiteSpace(input.GpuLine))
            return source;
        return source.Where(x => x.Name.Contains(input.GpuLine.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class StorageFilterStrategy : ICatalogFilterStrategy
{
    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (string.IsNullOrWhiteSpace(input.Storage))
            return source;
        return source.Where(x => x.Name.Contains(input.Storage.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class AccessoryFilterStrategy : ICatalogFilterStrategy
{
    private static readonly HashSet<string> AccessoryCategoryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACCESSORY", "KEYBOARD", "MOUSE", "HEADSET", "FAN", "CHAIR", "COOLER_AIR", "COOLER_AIO"
    };

    public IEnumerable<CatalogProductRow> Apply(IEnumerable<CatalogProductRow> source, CatalogFilterInput input, CatalogFilterContext context)
    {
        if (!input.AccessoryOnly)
            return source;
        return source.Where(x => AccessoryCategoryCodes.Contains(x.CategoryCode));
    }
}

