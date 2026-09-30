using ban_link_kien_PC.Domain.Builds;
using ban_link_kien_PC.Domain.Catalog;
using ban_link_kien_PC.Domain.Compatibility;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

public sealed class BuildPcController : Controller
{
    private readonly IComponentCatalogQuery _catalog;
    private readonly BuildPcFacade _facade;
    private readonly BuildEditorFacade _editor;
    private readonly CartManager _cart;
    private readonly PcStoreDbContext _db;

    public BuildPcController(IComponentCatalogQuery catalog, BuildPcFacade facade, BuildEditorFacade editor, CartManager cart, PcStoreDbContext db)
    {
        _catalog = catalog;
        _facade = facade;
        _editor = editor;
        _cart = cart;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var categoriesOrder = new[]
        {
            "CPU","MAINBOARD","RAM","SSD","HDD","GPU","PSU","CASE",
            "MONITOR","KEYBOARD","MOUSE","HEADSET","COOLER_AIR","COOLER_AIO","FAN","CHAIR","ACCESSORY"
        };

        var model = new BuildPcViewModel
        {
            Categories = categoriesOrder.ToList(),
            Components = await _catalog.SearchAsync(null, null, null, null, ct),
            Presets = _editor.GetPresetOptions().ToList(),
            Ecosystems = _editor.GetEcosystemOptions().ToList()
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Preview([FromBody] BuildPcSelectionDto dto, CancellationToken ct)
    {
        var selection = ToSelection(dto);
        var result = await _facade.ValidateAsync(selection, ct);
        var total = await _facade.CalculateTotalVndAsync(selection, ct);
        return Json(new { result.IsValid, result.Issues, result.Matches, totalPriceVnd = total });
    }

    [HttpPost]
    public async Task<IActionResult> AddToCart([FromBody] BuildPcSelectionDto dto, CancellationToken ct)
    {
        var cartKey = CartKeyResolver.GetOrCreateCartKey(HttpContext);
        var snapshot = await _editor.GetSnapshotAsync(cartKey, ct);

        var items = (dto.Items ?? [])
            .Where(x => x.ComponentId > 0 && x.Qty > 0)
            .Select(x => new BuildCartItemInput(x.ComponentId, x.Qty))
            .ToList();

        if (items.Count == 0 && snapshot.Selected.Count > 0)
        {
            items = snapshot.Selected
                .Select(x => new BuildCartItemInput(x.ComponentId, x.Qty))
                .ToList();
        }

        if (items.Count == 0)
        {
            void AddIfHas(int? id, int qty)
            {
                if (id is null) return;
                items.Add(new BuildCartItemInput(id.Value, qty));
            }

            AddIfHas(dto.CpuId, 1);
            AddIfHas(dto.MainboardId, 1);
            AddIfHas(dto.RamId, dto.RamQty);
            AddIfHas(dto.GpuId, 1);
            AddIfHas(dto.PsuId, 1);
        }

        if (items.Count == 0)
            return BadRequest(new { Message = "Chưa có linh kiện nào trong cấu hình để thêm vào giỏ." });

        if (snapshot.Selected.Count > 0 && !snapshot.IsValid)
        {
            return BadRequest(new
            {
                Message = "Cấu hình chưa tương thích — không thể thêm vào giỏ hàng.",
                Issues = snapshot.Issues
            });
        }

        // Fallback when workspace empty but DTO has core parts (Preview-style payloads).
        if (snapshot.Selected.Count == 0)
        {
            var selection = ToSelection(dto);
            var validate = await _facade.ValidateAsync(selection, ct);
            if (!validate.IsValid)
            {
                return BadRequest(new
                {
                    Message = "Cấu hình chưa tương thích — không thể thêm vào giỏ hàng.",
                    Issues = validate.Issues
                });
            }
        }

        var buildTitle = string.IsNullOrWhiteSpace(dto.BuildName) ? null : dto.BuildName!.Trim();
        _cart.AddBuildCard(cartKey, items, buildTitle);
        return Ok(new { Message = "Đã thêm 1 cấu hình build vào giỏ hàng." });
    }

    private static BuildSelection ToSelection(BuildPcSelectionDto dto)
    {
        var extras = (dto.Items ?? [])
            .Where(x => x.ComponentId > 0 && x.Qty > 0)
            .Where(x =>
                x.ComponentId != dto.CpuId
                && x.ComponentId != dto.MainboardId
                && x.ComponentId != dto.RamId
                && x.ComponentId != dto.GpuId
                && x.ComponentId != dto.PsuId)
            .Select(x => new BuildLine(x.ComponentId, x.Qty))
            .ToList();

        return new BuildSelection(
            dto.CpuId,
            dto.MainboardId,
            dto.RamId,
            Math.Max(1, dto.RamQty),
            dto.GpuId,
            dto.PsuId,
            ExtraLines: extras.Count > 0 ? extras : null);
    }

    [HttpPost]
    public async Task<IActionResult> ApplyPreset([FromBody] ApplyPresetDto dto, CancellationToken ct)
    {
        var key = CartKeyResolver.GetOrCreateCartKey(HttpContext);
        var snapshot = await _editor.ApplyPresetAsync(key, dto.PresetCode, ct);
        return Json(snapshot);
    }

    [HttpPost]
    public async Task<IActionResult> ApplyCommand([FromBody] BuildCommandDto dto, CancellationToken ct)
    {
        var key = CartKeyResolver.GetOrCreateCartKey(HttpContext);
        var snapshot = await _editor.ApplyCommandAsync(key, dto.CategoryCode, dto.ComponentId, dto.Qty, ct);
        return Json(snapshot);
    }

    [HttpPost]
    public async Task<IActionResult> ApplyEcosystem([FromBody] ApplyEcosystemDto dto, CancellationToken ct)
    {
        var key = CartKeyResolver.GetOrCreateCartKey(HttpContext);
        var snapshot = await _editor.ApplyEcosystemAsync(key, dto.EcosystemCode, ct);
        return Json(snapshot);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateServices([FromBody] ServiceOptionsDto dto, CancellationToken ct)
    {
        var key = CartKeyResolver.GetOrCreateCartKey(HttpContext);
        var snapshot = await _editor.UpdateServicesAsync(key, dto.IncludeOverclock, dto.IncludeExtendedWarranty, ct);
        return Json(snapshot);
    }

    [HttpPost]
    public async Task<IActionResult> Undo(CancellationToken ct)
    {
        var key = CartKeyResolver.GetOrCreateCartKey(HttpContext);
        var snapshot = await _editor.UndoAsync(key, ct);
        return Json(snapshot);
    }

    [HttpPost]
    public async Task<IActionResult> Redo(CancellationToken ct)
    {
        var key = CartKeyResolver.GetOrCreateCartKey(HttpContext);
        var snapshot = await _editor.RedoAsync(key, ct);
        return Json(snapshot);
    }

    [HttpPost]
    public async Task<IActionResult> SaveConfiguration([FromBody] SaveConfigurationDto dto, CancellationToken ct)
    {
        var key = CartKeyResolver.GetOrCreateCartKey(HttpContext);
        var result = await _editor.SaveBuildAsync(key, dto.Name, ct);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> SavedConfigurations(CancellationToken ct)
    {
        var configs = await _db.BuildConfigurations.AsNoTracking()
            .Include(x => x.Items)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(80)
            .ToListAsync(ct);

        var componentIds = configs.SelectMany(x => x.Items).Select(x => x.ComponentId).Distinct().ToList();
        var components = await _db.Components.AsNoTracking()
            .Where(x => componentIds.Contains(x.ComponentId))
            .Select(x => new { x.ComponentId, x.Name, x.PriceVnd })
            .ToDictionaryAsync(x => x.ComponentId, ct);

        var categoryById = await _db.ComponentCategories.AsNoTracking()
            .ToDictionaryAsync(x => x.ComponentCategoryId, x => x.DisplayName, ct);

        var rows = configs.Select(cfg =>
        {
            var items = cfg.Items.Select(i =>
            {
                components.TryGetValue(i.ComponentId, out var comp);
                categoryById.TryGetValue(i.ComponentCategoryId, out var catName);
                var unit = comp?.PriceVnd ?? 0m;
                return new SavedBuildItemRow(
                    catName ?? "N/A",
                    comp?.Name ?? $"Component #{i.ComponentId}",
                    i.Qty,
                    unit,
                    unit * i.Qty
                );
            }).OrderBy(x => x.Category).ToList();

            return new SavedBuildRow(
                cfg.BuildConfigurationId,
                cfg.Name,
                cfg.CreatedAtUtc,
                items,
                items.Sum(x => x.LineTotalVnd)
            );
        }).ToList();

        var vm = new SavedBuildListViewModel
        {
            IsGuest = User.Identity?.IsAuthenticated != true,
            Rows = rows
        };

        return View(vm);
    }
}

public sealed record BuildPcSelectionDto(
    int? CpuId,
    int? MainboardId,
    int? RamId,
    int RamQty,
    int? GpuId,
    int? PsuId,
    List<BuildCartItemDto>? Items = null,
    string? BuildName = null);
public sealed record BuildCartItemDto(int ComponentId, int Qty);

public sealed class BuildPcViewModel
{
    public List<string> Categories { get; set; } = [];
    public IReadOnlyList<ComponentListItem> Components { get; set; } = Array.Empty<ComponentListItem>();
    public List<PresetViewModel> Presets { get; set; } = [];
    public List<EcosystemViewModel> Ecosystems { get; set; } = [];
}

public sealed record ApplyPresetDto(string PresetCode);
public sealed record BuildCommandDto(string CategoryCode, int? ComponentId, int Qty);
public sealed record ApplyEcosystemDto(string EcosystemCode);
public sealed record ServiceOptionsDto(bool IncludeOverclock, bool IncludeExtendedWarranty);
public sealed record SaveConfigurationDto(string? Name);

public sealed class SavedBuildListViewModel
{
    public bool IsGuest { get; set; }
    public List<SavedBuildRow> Rows { get; set; } = [];
}

public sealed record SavedBuildRow(
    int BuildConfigurationId,
    string Name,
    DateTime CreatedAtUtc,
    List<SavedBuildItemRow> Items,
    decimal TotalPriceVnd);

public sealed record SavedBuildItemRow(
    string Category,
    string ComponentName,
    int Qty,
    decimal UnitPriceVnd,
    decimal LineTotalVnd);

