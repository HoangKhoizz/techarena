namespace ban_link_kien_PC.Models;

public sealed class HomeProductCardVm
{
    public int ComponentId { get; init; }
    public string Sku { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Price { get; init; } = "";
    public string Specs { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public bool BestSeller { get; init; }
    public bool IsHot { get; init; }
    public bool IsSoldOut { get; init; }
}

public sealed class HomeCategoryCardVm
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string CategoryCode { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string IconClass { get; init; } = "bi-controller";
}

public sealed class HomeIndexViewModel
{
    public string HeroImageUrl { get; init; } = "/images/placeholder-product.svg";
    public IReadOnlyList<HomeCategoryCardVm> Categories { get; init; } = [];
    public IReadOnlyList<HomeProductCardVm> HotProducts { get; init; } = [];
    public IReadOnlyList<HomeProductCardVm> BestSellers { get; init; } = [];
    public IReadOnlyList<HomeProductCardVm> FeaturedAll { get; init; } = [];
    public IReadOnlyList<HomeProductCardVm> FeaturedGaming { get; init; } = [];
    public IReadOnlyList<HomeProductCardVm> FeaturedGraphics { get; init; } = [];
    public IReadOnlyList<HomeProductCardVm> FeaturedOffice { get; init; } = [];
    public IReadOnlyList<HomeProductCardVm> PcGaming { get; init; } = [];
    public IReadOnlyList<HomeProductCardVm> Components { get; init; } = [];
}
