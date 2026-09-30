using ban_link_kien_PC.Domain.Compatibility;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Builds;

// Facade pattern: a single entrypoint for the front-end.
public sealed class BuildPcFacade
{
    private readonly PcStoreDbContext _db;
    private readonly IEnumerable<ICompatibilityRule> _rules;

    public BuildPcFacade(PcStoreDbContext db, IEnumerable<ICompatibilityRule> rules)
    {
        _db = db;
        _rules = rules;
    }

    public async Task<CompatibilityResult> ValidateAsync(BuildSelection selection, CancellationToken ct = default)
    {
        var allIssues = new List<CompatibilityIssue>();
        var allMatches = new List<CompatibilityMatch>();
        foreach (var rule in _rules)
        {
            var outcome = await rule.EvaluateAsync(selection, ct);
            allIssues.AddRange(outcome.Issues);
            allMatches.AddRange(outcome.Matches);
        }

        return allIssues.Count == 0
            ? CompatibilityResult.Ok(allMatches.ToArray())
            : CompatibilityResult.Fail(allIssues, allMatches);
    }

    public async Task<decimal> CalculateTotalVndAsync(BuildSelection selection, CancellationToken ct = default)
    {
        var ids = new List<(int id, int qty)>();
        if (selection.CpuId is not null) ids.Add((selection.CpuId.Value, 1));
        if (selection.MainboardId is not null) ids.Add((selection.MainboardId.Value, 1));
        if (selection.RamId is not null) ids.Add((selection.RamId.Value, Math.Max(1, selection.RamQty)));
        if (selection.GpuId is not null) ids.Add((selection.GpuId.Value, 1));
        if (selection.PsuId is not null) ids.Add((selection.PsuId.Value, 1));
        if (selection.SsdId is not null) ids.Add((selection.SsdId.Value, 1));
        if (selection.CaseId is not null) ids.Add((selection.CaseId.Value, 1));

        if (selection.ExtraLines is not null)
        {
            foreach (var line in selection.ExtraLines)
            {
                if (line.ComponentId <= 0 || line.Qty <= 0) continue;
                if (ids.Any(x => x.id == line.ComponentId)) continue;
                ids.Add((line.ComponentId, line.Qty));
            }
        }

        if (ids.Count == 0) return 0m;

        var priceMap = await _db.Components.AsNoTracking()
            .Where(c => ids.Select(x => x.id).Contains(c.ComponentId))
            .Select(c => new { c.ComponentId, c.PriceVnd })
            .ToDictionaryAsync(x => x.ComponentId, x => x.PriceVnd, ct);

        decimal total = 0m;
        foreach (var (id, qty) in ids)
        {
            if (priceMap.TryGetValue(id, out var price))
                total += price * qty;
        }
        return total;
    }
}
