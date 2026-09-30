using System.Collections.Concurrent;
using ban_link_kien_PC.Domain.Components;
using ban_link_kien_PC.Domain.Compatibility;
using ban_link_kien_PC.Domain.Factories;
using ban_link_kien_PC.Domain.Services;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Builds;

public sealed class BuildWorkspaceManager
{
    private readonly ConcurrentDictionary<string, BuildWorkspaceState> _workspaces = new(StringComparer.OrdinalIgnoreCase);

    public BuildWorkspaceState GetOrCreate(string workspaceKey)
        => _workspaces.GetOrAdd(workspaceKey, _ => new BuildWorkspaceState());
}

public sealed class BuildWorkspaceState
{
    public BuildDraft Draft { get; } = new();
    public BuildCommandHistory History { get; } = new();
    public bool IncludeOverclock { get; set; }
    public bool IncludeExtendedWarranty { get; set; }
}

public interface IBuildPresetCreator
{
    string Code { get; }
    string DisplayName { get; }
    IReadOnlyList<string> Categories { get; }
}

public sealed class GamingPresetCreator : IBuildPresetCreator
{
    public string Code => "GAMING";
    public string DisplayName => "Gaming preset";
    public IReadOnlyList<string> Categories => ["CPU", "MAINBOARD", "RAM", "GPU", "SSD", "PSU", "CASE"];
}

public sealed class WorkstationPresetCreator : IBuildPresetCreator
{
    public string Code => "WORKSTATION";
    public string DisplayName => "Workstation preset";
    public IReadOnlyList<string> Categories => ["CPU", "MAINBOARD", "RAM", "SSD", "GPU", "PSU"];
}

public sealed class OfficePresetCreator : IBuildPresetCreator
{
    public string Code => "OFFICE";
    public string DisplayName => "Office preset";
    public IReadOnlyList<string> Categories => ["CPU", "MAINBOARD", "RAM", "SSD", "PSU"];
}

public sealed class StreamingPresetCreator : IBuildPresetCreator
{
    public string Code => "STREAMING";
    public string DisplayName => "Streaming preset";
    public IReadOnlyList<string> Categories => ["CPU", "MAINBOARD", "RAM", "GPU", "SSD", "PSU", "CASE", "MONITOR", "HEADSET"];
}

public sealed class BudgetPresetCreator : IBuildPresetCreator
{
    public string Code => "BUDGET";
    public string DisplayName => "Budget preset";
    public IReadOnlyList<string> Categories => ["CPU", "MAINBOARD", "RAM", "SSD", "PSU", "CASE"];
}

public sealed class BuildEditorFacade
{
    private readonly PcStoreDbContext _db;
    private readonly BuildPcFacade _buildFacade;
    private readonly BuildWorkspaceManager _workspace;
    private readonly ComponentRequestCreator _requestCreator;
    private readonly IEnumerable<IBuildPresetCreator> _presetCreators;
    private readonly IEnumerable<IEcosystemFactory> _ecosystemFactories;
    private readonly CompatibleBuildAssembler _assembler;

    public BuildEditorFacade(
        PcStoreDbContext db,
        BuildPcFacade buildFacade,
        BuildWorkspaceManager workspace,
        ComponentRequestCreator requestCreator,
        IEnumerable<IBuildPresetCreator> presetCreators,
        IEnumerable<IEcosystemFactory> ecosystemFactories,
        CompatibleBuildAssembler assembler)
    {
        _db = db;
        _buildFacade = buildFacade;
        _workspace = workspace;
        _requestCreator = requestCreator;
        _presetCreators = presetCreators;
        _ecosystemFactories = ecosystemFactories;
        _assembler = assembler;
    }

    public IReadOnlyList<PresetViewModel> GetPresetOptions()
        => _presetCreators.Select(x => new PresetViewModel(x.Code, x.DisplayName)).ToList();

    public IReadOnlyList<EcosystemViewModel> GetEcosystemOptions()
        => _ecosystemFactories.Select(x => new EcosystemViewModel(x.Code, x.Name)).ToList();

    public async Task<BuildEditorSnapshot> ApplyPresetAsync(string workspaceKey, string presetCode, CancellationToken ct = default)
    {
        var preset = _presetCreators.FirstOrDefault(x => x.Code.Equals(presetCode, StringComparison.OrdinalIgnoreCase))
            ?? _presetCreators.First(x => x.Code.Equals("GAMING", StringComparison.OrdinalIgnoreCase));

        var ws = _workspace.GetOrCreate(workspaceKey);
        ClearDraft(ws);

        var core = await _assembler.AssemblePresetAsync(preset.Code, preset.Categories, ct);
        var byCat = core.ToDictionary(x => x.CategoryCode, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var category in preset.Categories)
        {
            if (!byCat.TryGetValue(category, out var part))
            {
                var accessory = await _assembler.PickAccessoryAsync(category, preferredBrand: null, ct);
                if (accessory is null) continue;
                part = accessory;
            }

            var req = _requestCreator.Create(part.CategoryCode, part.ComponentId, part.Qty);
            ws.History.Execute(new SelectComponentCommand(req.CategoryCode, req.ComponentId, req.Qty), ws.Draft);
        }

        return await BuildSnapshotAsync(ws, ct);
    }

    public async Task<BuildEditorSnapshot> ApplyEcosystemAsync(string workspaceKey, string ecosystemCode, CancellationToken ct = default)
    {
        var eco = _ecosystemFactories.FirstOrDefault(x => x.Code.Equals(ecosystemCode, StringComparison.OrdinalIgnoreCase));
        if (eco is null)
            return await BuildSnapshotAsync(_workspace.GetOrCreate(workspaceKey), ct);

        var ws = _workspace.GetOrCreate(workspaceKey);
        ClearDraft(ws);

        var parts = await _assembler.AssembleEcosystemAsync(eco, ct);
        foreach (var part in parts)
        {
            var qty = part.CategoryCode.Equals("RAM", StringComparison.OrdinalIgnoreCase)
                ? await _assembler.ResolveRamQtyAsync(part.ComponentId, part.Qty, ct)
                : Math.Max(1, part.Qty);
            var req = _requestCreator.Create(part.CategoryCode, part.ComponentId, qty);
            ws.History.Execute(new SelectComponentCommand(req.CategoryCode, req.ComponentId, req.Qty), ws.Draft);
        }

        return await BuildSnapshotAsync(ws, ct);
    }

    public async Task<BuildEditorSnapshot> ApplyCommandAsync(
        string workspaceKey,
        string categoryCode,
        int? componentId,
        int qty,
        CancellationToken ct = default)
    {
        var ws = _workspace.GetOrCreate(workspaceKey);
        if (componentId.HasValue && componentId.Value > 0)
        {
            var resolvedQty = categoryCode.Equals("RAM", StringComparison.OrdinalIgnoreCase)
                ? await _assembler.ResolveRamQtyAsync(componentId.Value, qty, ct)
                : Math.Max(1, qty);
            var req = _requestCreator.Create(categoryCode, componentId.Value, resolvedQty);
            ws.History.Execute(new SelectComponentCommand(req.CategoryCode, req.ComponentId, req.Qty), ws.Draft);
        }
        else
        {
            ws.History.Execute(new RemoveCategoryCommand(categoryCode), ws.Draft);
        }

        return await BuildSnapshotAsync(ws, ct);
    }

    private static void ClearDraft(BuildWorkspaceState ws)
    {
        ws.Draft.Clear();
        ws.History.Reset();
    }

    public async Task<BuildEditorSnapshot> UpdateServicesAsync(
        string workspaceKey,
        bool includeOverclock,
        bool includeExtendedWarranty,
        CancellationToken ct = default)
    {
        var ws = _workspace.GetOrCreate(workspaceKey);
        ws.IncludeOverclock = includeOverclock;
        ws.IncludeExtendedWarranty = includeExtendedWarranty;
        return await BuildSnapshotAsync(ws, ct);
    }

    public async Task<BuildEditorSnapshot> UndoAsync(string workspaceKey, CancellationToken ct = default)
    {
        var ws = _workspace.GetOrCreate(workspaceKey);
        ws.History.Undo(ws.Draft);
        return await BuildSnapshotAsync(ws, ct);
    }

    public async Task<BuildEditorSnapshot> RedoAsync(string workspaceKey, CancellationToken ct = default)
    {
        var ws = _workspace.GetOrCreate(workspaceKey);
        ws.History.Redo(ws.Draft);
        return await BuildSnapshotAsync(ws, ct);
    }

    public Task<BuildEditorSnapshot> GetSnapshotAsync(string workspaceKey, CancellationToken ct = default)
        => BuildSnapshotAsync(_workspace.GetOrCreate(workspaceKey), ct);

    public async Task<SaveBuildResult> SaveBuildAsync(string workspaceKey, string? name, CancellationToken ct = default)
    {
        var ws = _workspace.GetOrCreate(workspaceKey);
        if (ws.Draft.Selected.Count == 0)
            return new SaveBuildResult(false, null, "Chua co linh kien de luu cau hinh.");

        var builder = new BuildConfigurationBuilder().Named(name ?? "My build");
        foreach (var (categoryCode, data) in ws.Draft.Selected)
            builder.Add(categoryCode, data.componentId, data.qty);

        var config = builder.Build();
        var categoryMap = await _db.ComponentCategories.AsNoTracking()
            .ToDictionaryAsync(x => x.Code, x => x.ComponentCategoryId, StringComparer.OrdinalIgnoreCase, ct);

        var entity = new BuildConfigurationEntity
        {
            Name = config.Name,
            CreatedAtUtc = DateTime.UtcNow
        };

        entity.Items = config.Selected
            .Where(x => categoryMap.ContainsKey(x.CategoryCode))
            .Select(x => new BuildConfigurationItemEntity
            {
                ComponentCategoryId = categoryMap[x.CategoryCode],
                ComponentId = x.ComponentId,
                Qty = x.Qty
            }).ToList();

        _db.BuildConfigurations.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new SaveBuildResult(true, entity.BuildConfigurationId, $"Da luu cau hinh #{entity.BuildConfigurationId}.");
    }

    private async Task<BuildEditorSnapshot> BuildSnapshotAsync(BuildWorkspaceState ws, CancellationToken ct)
    {
        var selection = ToBuildSelection(ws.Draft);
        var validate = await _buildFacade.ValidateAsync(selection, ct);
        var total = await _buildFacade.CalculateTotalVndAsync(selection, ct);

        var selected = ws.Draft.Selected
            .Select(x => new SelectedDraftItem(x.Key, x.Value.componentId, x.Value.qty))
            .OrderBy(x => x.CategoryCode)
            .ToList();

        var ids = selected.Select(x => x.ComponentId).Distinct().ToList();
        var catalog = await _db.Components.AsNoTracking()
            .Where(x => ids.Contains(x.ComponentId))
            .Select(x => new { x.ComponentId, x.Name, x.PriceVnd })
            .ToDictionaryAsync(x => x.ComponentId, x => (x.Name, x.PriceVnd), ct);

        var config = new ComputerConfiguration
        {
            Name = "Workspace Build",
            Selected = selected.Select(x => new SelectedComponent(x.ComponentId, x.CategoryCode, x.Qty)).ToList()
        };
        var composite = config.AsComposite("PC Composite", id =>
        {
            if (catalog.TryGetValue(id, out var hit))
                return hit;
            return ($"Component #{id}", 0m);
        });
        var compositeItems = composite.Children
            .OfType<ComponentLeaf>()
            .Select(x => new CompositeLine(x.DisplayName, x.Qty, x.GetPriceVnd()))
            .ToList();
        var compositeTotal = composite.GetPriceVnd();

        IServicePricing service = new BaseBuildService();
        if (ws.IncludeOverclock)
            service = new OverclockServiceDecorator(service);
        if (ws.IncludeExtendedWarranty)
            service = new ExtendedWarrantyDecorator(service);
        var serviceAddOn = service.AddOnPriceVnd;
        var totalWithService = compositeTotal + serviceAddOn;

        return new BuildEditorSnapshot(
            selected,
            total,
            compositeTotal,
            service.Name,
            serviceAddOn,
            totalWithService,
            ws.IncludeOverclock,
            ws.IncludeExtendedWarranty,
            compositeItems,
            validate.IsValid,
            validate.Issues,
            validate.Matches,
            ws.History.CanUndo,
            ws.History.CanRedo);
    }

    private static BuildSelection ToBuildSelection(BuildDraft draft)
    {
        int? GetId(string code)
            => draft.Selected.TryGetValue(code, out var v) ? v.componentId : null;
        int GetQty(string code)
            => draft.Selected.TryGetValue(code, out var v) ? Math.Max(1, v.qty) : 1;

        var coreCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CPU", "MAINBOARD", "RAM", "GPU", "PSU", "SSD", "CASE"
        };

        var extras = draft.Selected
            .Where(x => !coreCodes.Contains(x.Key))
            .Select(x => new BuildLine(x.Value.componentId, Math.Max(1, x.Value.qty)))
            .ToList();

        return new BuildSelection(
            GetId("CPU"),
            GetId("MAINBOARD"),
            GetId("RAM"),
            GetQty("RAM"),
            GetId("GPU"),
            GetId("PSU"),
            GetId("SSD"),
            GetId("CASE"),
            extras);
    }
}

public sealed record PresetViewModel(string Code, string DisplayName);
public sealed record EcosystemViewModel(string Code, string DisplayName);
public sealed record SelectedDraftItem(string CategoryCode, int ComponentId, int Qty);
public sealed record CompositeLine(string DisplayName, int Qty, decimal LineTotalVnd);
public sealed record BuildEditorSnapshot(
    IReadOnlyList<SelectedDraftItem> Selected,
    decimal TotalPriceVnd,
    decimal CompositeTotalVnd,
    string ServiceName,
    decimal ServiceAddOnVnd,
    decimal TotalWithServiceVnd,
    bool IncludeOverclock,
    bool IncludeExtendedWarranty,
    IReadOnlyList<CompositeLine> CompositeItems,
    bool IsValid,
    IReadOnlyList<CompatibilityIssue> Issues,
    IReadOnlyList<CompatibilityMatch> Matches,
    bool CanUndo,
    bool CanRedo);
public sealed record SaveBuildResult(bool Success, int? BuildConfigurationId, string Message);
