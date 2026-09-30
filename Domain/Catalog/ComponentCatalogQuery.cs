using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Catalog;

public sealed record ComponentListItem(
    int ComponentId,
    string CategoryCode,
    string Sku,
    string Name,
    string? Brand,
    decimal PriceVnd,
    int StockQty,
    string ImageUrl);

public interface IComponentCatalogQuery
{
    Task<IReadOnlyList<ComponentListItem>> SearchAsync(
        string? categoryCode,
        string? socketCode,
        string? chipset,
        string? ramStandardCode,
        CancellationToken ct = default);
}

public sealed class ComponentCatalogQuery : IComponentCatalogQuery
{
    private readonly PcStoreDbContext _db;
    public ComponentCatalogQuery(PcStoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<ComponentListItem>> SearchAsync(
        string? categoryCode,
        string? socketCode,
        string? chipset,
        string? ramStandardCode,
        CancellationToken ct = default)
    {
        categoryCode = string.IsNullOrWhiteSpace(categoryCode) ? null : categoryCode.Trim().ToUpperInvariant();
        socketCode = string.IsNullOrWhiteSpace(socketCode) ? null : socketCode.Trim().ToUpperInvariant();
        chipset = string.IsNullOrWhiteSpace(chipset) ? null : chipset.Trim().ToUpperInvariant();
        ramStandardCode = string.IsNullOrWhiteSpace(ramStandardCode) ? null : ramStandardCode.Trim().ToUpperInvariant();

        var q = _db.Components.AsNoTracking()
            .Where(x => x.IsActive && x.StockQty > 0)
            .Join(_db.ComponentCategories.AsNoTracking(),
                c => c.ComponentCategoryId,
                cat => cat.ComponentCategoryId,
                (c, cat) => new { c, cat })
            .Select(x => new
            {
                x.c.ComponentId,
                CategoryCode = x.cat.Code,
                x.c.Sku,
                x.c.Name,
                BrandName = _db.Brands.Where(b => b.BrandId == x.c.BrandId).Select(b => b.Name).FirstOrDefault(),
                x.c.PriceVnd,
                x.c.StockQty,
                x.c.ImageUrl
            });

        if (categoryCode is not null)
            q = q.Where(x => x.CategoryCode == categoryCode);

        if (socketCode is not null)
        {
            // socket filter applies to CPU and MAINBOARD
            q = q.Where(x =>
                (x.CategoryCode == "CPU" && _db.CpuSpecs.Any(s => s.ComponentId == x.ComponentId && _db.Sockets.Any(sk => sk.SocketId == s.SocketId && sk.Code == socketCode)))
                || (x.CategoryCode == "MAINBOARD" && _db.MainboardSpecs.Any(s => s.ComponentId == x.ComponentId && _db.Sockets.Any(sk => sk.SocketId == s.SocketId && sk.Code == socketCode)))
            );
        }

        if (chipset is not null)
        {
            q = q.Where(x => x.CategoryCode == "MAINBOARD"
                             && _db.MainboardSpecs.Any(s => s.ComponentId == x.ComponentId && s.Chipset.ToUpper() == chipset));
        }

        if (ramStandardCode is not null)
        {
            q = q.Where(x =>
                (x.CategoryCode == "MAINBOARD" && _db.MainboardSpecs.Any(s => s.ComponentId == x.ComponentId
                    && _db.RamStandards.Any(r => r.RamStandardId == s.RamStandardId && r.Code == ramStandardCode)))
                || (x.CategoryCode == "RAM" && _db.RamSpecs.Any(s => s.ComponentId == x.ComponentId
                    && _db.RamStandards.Any(r => r.RamStandardId == s.RamStandardId && r.Code == ramStandardCode)))
            );
        }

        var rows = await q
            .OrderBy(x => x.CategoryCode).ThenBy(x => x.PriceVnd)
            .Take(200)
            .ToListAsync(ct);

        return rows.Select(x => new ComponentListItem(
            x.ComponentId,
            x.CategoryCode,
            x.Sku,
            x.Name,
            x.BrandName,
            x.PriceVnd,
            x.StockQty,
            CatalogImageResolver.Resolve(x.ComponentId, x.CategoryCode, x.Sku, x.ImageUrl))).ToList();
    }
}

