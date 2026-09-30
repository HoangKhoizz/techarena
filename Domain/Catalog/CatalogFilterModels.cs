namespace ban_link_kien_PC.Domain.Catalog;

public sealed record CatalogProductRow(
    int ComponentId,
    string Sku,
    string Name,
    string ImageUrl,
    decimal PriceVnd,
    int StockQty,
    bool IsHot,
    bool IsBestSeller,
    string CategoryCode,
    string CategoryName,
    string? BrandName)
{
    public bool IsSoldOut => StockQty <= 0;
}

public sealed class CatalogFilterInput
{
    public string? Keyword { get; set; }
    public string? CategoryCode { get; set; }
    public decimal? MinPriceVnd { get; set; }
    public decimal? MaxPriceVnd { get; set; }
    public string? Brand { get; set; }
    public string? CpuLine { get; set; }
    public int? RamCapacityGb { get; set; }
    public string? GpuLine { get; set; }
    public string? Storage { get; set; }
    public bool AccessoryOnly { get; set; }
}

public sealed class CatalogPageViewModel
{
    public CatalogFilterInput Filter { get; init; } = new();
    public List<CatalogCategoryOption> Categories { get; init; } = [];
    public List<string> Brands { get; init; } = [];
    public List<string> CpuLines { get; init; } = [];
    public List<int> RamCapacitiesGb { get; init; } = [];
    public List<string> GpuLines { get; init; } = [];
    public List<string> StorageValues { get; init; } = [];
    public List<CatalogProductRow> Products { get; init; } = [];
}

public sealed record CatalogCategoryOption(string Code, string DisplayName);

public sealed class CatalogDetailViewModel
{
    public int ComponentId { get; init; }
    public string Sku { get; init; } = "";
    public string Name { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public decimal PriceVnd { get; init; }
    public int StockQty { get; init; }
    public bool IsHot { get; init; }
    public bool IsBestSeller { get; init; }
    public bool IsSoldOut => StockQty <= 0;
    public string CategoryCode { get; init; } = "";
    public string CategoryName { get; init; } = "";
    public string? BrandName { get; init; }
    public List<KeyValuePair<string, string>> Specs { get; init; } = [];
}

public sealed record CatalogSuggestItem(
    int ComponentId,
    string Sku,
    string Name,
    string ImageUrl,
    decimal PriceVnd,
    string CategoryCode,
    string Slug);

