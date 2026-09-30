using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Catalog;

public interface IProductDetailSpecStrategy
{
    Task AppendSpecsAsync(int componentId, List<KeyValuePair<string, string>> specs, CancellationToken ct = default);
}

public sealed class ProductDetailFacade
{
    private readonly PcStoreDbContext _db;
    private readonly IEnumerable<IProductDetailSpecStrategy> _specStrategies;

    public ProductDetailFacade(PcStoreDbContext db, IEnumerable<IProductDetailSpecStrategy> specStrategies)
    {
        _db = db;
        _specStrategies = specStrategies;
    }

    public async Task<CatalogDetailViewModel?> BuildAsync(int componentId, CancellationToken ct = default)
    {
        var row = await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
            from b in bj.DefaultIfEmpty()
            where c.ComponentId == componentId && c.IsActive
            select new
            {
                c.ComponentId,
                c.Sku,
                c.Name,
                c.PriceVnd,
                c.StockQty,
                c.IsHot,
                c.IsBestSeller,
                c.ImageUrl,
                CategoryCode = cat.Code,
                CategoryName = cat.DisplayName,
                BrandName = b != null ? b.Name : null
            }
        ).FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        var specs = new List<KeyValuePair<string, string>>();
        foreach (var strategy in _specStrategies)
            await strategy.AppendSpecsAsync(componentId, specs, ct);
        AppendPrebuiltPcSpecs(row.CategoryCode, row.Name, row.Sku, specs);

        return new CatalogDetailViewModel
        {
            ComponentId = row.ComponentId,
            Sku = row.Sku,
            Name = row.Name,
            ImageUrl = CatalogImageResolver.Resolve(row.ComponentId, row.CategoryCode, row.Sku, row.ImageUrl),
            PriceVnd = row.PriceVnd,
            StockQty = row.StockQty,
            IsHot = row.IsHot,
            IsBestSeller = row.IsBestSeller,
            CategoryCode = row.CategoryCode,
            CategoryName = row.CategoryName,
            BrandName = row.BrandName,
            Specs = specs
        };
    }

    private static void AppendPrebuiltPcSpecs(
        string categoryCode,
        string productName,
        string sku,
        List<KeyValuePair<string, string>> specs)
    {
        if (!string.Equals(categoryCode, "PC_PREBUILT", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(categoryCode, "PC_WORKSTATION", StringComparison.OrdinalIgnoreCase))
            return;

        var profile = string.Equals(categoryCode, "PC_WORKSTATION", StringComparison.OrdinalIgnoreCase)
            ? "Workstation 2D/3D"
            : "PC Build Sẵn";

        specs.Add(new("Dòng sản phẩm", profile));

        var parts = productName.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
            specs.Add(new("Cấu hình CPU", parts[0]));
        if (parts.Length > 1)
            specs.Add(new("RAM", parts[1]));
        if (parts.Length > 2)
            specs.Add(new("VGA", parts[2]));

        var skuParts = sku.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (skuParts.Length >= 4)
        {
            var chipset = string.Join("-", skuParts.Skip(2).Take(skuParts.Length - 3));
            var gpuCode = skuParts[^1];
            specs.Add(new("Mã cấu hình", $"{chipset} / {gpuCode}"));
        }
    }
}

public sealed class CpuProductDetailSpecStrategy : IProductDetailSpecStrategy
{
    private readonly PcStoreDbContext _db;

    public CpuProductDetailSpecStrategy(PcStoreDbContext db) => _db = db;

    public async Task AppendSpecsAsync(int componentId, List<KeyValuePair<string, string>> specs, CancellationToken ct = default)
    {
        var cpu = await (
            from s in _db.CpuSpecs.AsNoTracking()
            join sock in _db.Sockets.AsNoTracking() on s.SocketId equals sock.SocketId
            where s.ComponentId == componentId
            select new { s.Generation, s.TdpWatt, Socket = sock.DisplayName }
        ).FirstOrDefaultAsync(ct);

        if (cpu is null) return;
        specs.Add(new("Socket", cpu.Socket));
        specs.Add(new("Thế hệ", cpu.Generation ?? "N/A"));
        specs.Add(new("TDP", $"{cpu.TdpWatt}W"));
    }
}

public sealed class MainboardProductDetailSpecStrategy : IProductDetailSpecStrategy
{
    private readonly PcStoreDbContext _db;

    public MainboardProductDetailSpecStrategy(PcStoreDbContext db) => _db = db;

    public async Task AppendSpecsAsync(int componentId, List<KeyValuePair<string, string>> specs, CancellationToken ct = default)
    {
        var mb = await (
            from s in _db.MainboardSpecs.AsNoTracking()
            join sock in _db.Sockets.AsNoTracking() on s.SocketId equals sock.SocketId
            join ram in _db.RamStandards.AsNoTracking() on s.RamStandardId equals ram.RamStandardId
            join ff in _db.FormFactors.AsNoTracking() on s.FormFactorId equals ff.FormFactorId
            where s.ComponentId == componentId
            select new { s.Chipset, Socket = sock.DisplayName, Ram = ram.DisplayName, Form = ff.DisplayName, s.PcieSlotVersion }
        ).FirstOrDefaultAsync(ct);

        if (mb is null) return;
        specs.Add(new("Chipset", mb.Chipset));
        specs.Add(new("Socket", mb.Socket));
        specs.Add(new("RAM hỗ trợ", mb.Ram));
        specs.Add(new("Form factor", mb.Form));
        specs.Add(new("PCIe", mb.PcieSlotVersion ?? "N/A"));
    }
}

public sealed class RamProductDetailSpecStrategy : IProductDetailSpecStrategy
{
    private readonly PcStoreDbContext _db;

    public RamProductDetailSpecStrategy(PcStoreDbContext db) => _db = db;

    public async Task AppendSpecsAsync(int componentId, List<KeyValuePair<string, string>> specs, CancellationToken ct = default)
    {
        var ramSpec = await (
            from s in _db.RamSpecs.AsNoTracking()
            join rs in _db.RamStandards.AsNoTracking() on s.RamStandardId equals rs.RamStandardId
            where s.ComponentId == componentId
            select new { rs.DisplayName, s.CapacityGb, s.SpeedMhz, s.ModuleCount }
        ).FirstOrDefaultAsync(ct);

        if (ramSpec is null) return;
        specs.Add(new("Chuẩn RAM", ramSpec.DisplayName));
        specs.Add(new("Dung lượng", $"{ramSpec.CapacityGb}GB"));
        specs.Add(new("Tốc độ", ramSpec.SpeedMhz.HasValue ? $"{ramSpec.SpeedMhz}MHz" : "N/A"));
        if (ramSpec.ModuleCount > 0)
            specs.Add(new("Số thanh", ramSpec.ModuleCount >= 2 ? $"Kit {ramSpec.ModuleCount} thanh" : "1 thanh"));
    }
}

public sealed class GpuProductDetailSpecStrategy : IProductDetailSpecStrategy
{
    private readonly PcStoreDbContext _db;

    public GpuProductDetailSpecStrategy(PcStoreDbContext db) => _db = db;

    public async Task AppendSpecsAsync(int componentId, List<KeyValuePair<string, string>> specs, CancellationToken ct = default)
    {
        var gpu = await _db.GpuSpecs.AsNoTracking()
            .Where(x => x.ComponentId == componentId)
            .Select(x => new { x.TdpWatt })
            .FirstOrDefaultAsync(ct);

        if (gpu is null) return;
        specs.Add(new("TDP", $"{gpu.TdpWatt}W"));
    }
}

public sealed class PsuProductDetailSpecStrategy : IProductDetailSpecStrategy
{
    private readonly PcStoreDbContext _db;

    public PsuProductDetailSpecStrategy(PcStoreDbContext db) => _db = db;

    public async Task AppendSpecsAsync(int componentId, List<KeyValuePair<string, string>> specs, CancellationToken ct = default)
    {
        var psu = await _db.PsuSpecs.AsNoTracking()
            .Where(x => x.ComponentId == componentId)
            .Select(x => new { x.CapacityWatt, x.Efficiency })
            .FirstOrDefaultAsync(ct);

        if (psu is null) return;
        specs.Add(new("Công suất", $"{psu.CapacityWatt}W"));
        specs.Add(new("Chuẩn", psu.Efficiency ?? "N/A"));
    }
}

