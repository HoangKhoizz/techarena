using ban_link_kien_PC.Domain.Builds;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Compatibility;

public sealed record CompatibilityIssue(string Code, string Message);

public sealed record CompatibilityMatch(string Code, string Message);

public sealed record CompatibilityRuleOutcome(
    IReadOnlyList<CompatibilityIssue> Issues,
    IReadOnlyList<CompatibilityMatch> Matches)
{
    public static CompatibilityRuleOutcome Empty { get; } =
        new(Array.Empty<CompatibilityIssue>(), Array.Empty<CompatibilityMatch>());

    public static CompatibilityRuleOutcome Fail(params CompatibilityIssue[] issues) =>
        new(issues, Array.Empty<CompatibilityMatch>());

    public static CompatibilityRuleOutcome Ok(params CompatibilityMatch[] matches) =>
        new(Array.Empty<CompatibilityIssue>(), matches);
}

public sealed record CompatibilityResult(
    bool IsValid,
    IReadOnlyList<CompatibilityIssue> Issues,
    IReadOnlyList<CompatibilityMatch> Matches)
{
    public static CompatibilityResult Ok(params CompatibilityMatch[] matches) =>
        new(true, Array.Empty<CompatibilityIssue>(), matches);

    public static CompatibilityResult Fail(
        IReadOnlyList<CompatibilityIssue> issues,
        IReadOnlyList<CompatibilityMatch>? matches = null) =>
        new(false, issues, matches ?? Array.Empty<CompatibilityMatch>());
}

// Strategy pattern: each rule is a strategy.
public interface ICompatibilityRule
{
    Task<CompatibilityRuleOutcome> EvaluateAsync(BuildSelection selection, CancellationToken ct = default);
}

public sealed record BuildSelection(
    int? CpuId,
    int? MainboardId,
    int? RamId,
    int RamQty,
    int? GpuId,
    int? PsuId,
    int? SsdId = null,
    int? CaseId = null,
    IReadOnlyList<BuildLine>? ExtraLines = null);

public sealed record BuildLine(int ComponentId, int Qty);

public sealed class CpuMainboardSocketRule : ICompatibilityRule
{
    private readonly PcStoreDbContext _db;
    public CpuMainboardSocketRule(PcStoreDbContext db) => _db = db;

    public async Task<CompatibilityRuleOutcome> EvaluateAsync(BuildSelection selection, CancellationToken ct = default)
    {
        if (selection.CpuId is null || selection.MainboardId is null)
            return CompatibilityRuleOutcome.Empty;

        var cpu = await _db.CpuSpecs.AsNoTracking()
            .Where(x => x.ComponentId == selection.CpuId.Value)
            .Select(x => new { x.SocketId, SocketCode = x.Socket!.Code })
            .SingleOrDefaultAsync(ct);

        var mb = await _db.MainboardSpecs.AsNoTracking()
            .Where(x => x.ComponentId == selection.MainboardId.Value)
            .Select(x => new { x.SocketId, SocketCode = x.Socket!.Code })
            .SingleOrDefaultAsync(ct);

        if (cpu is null || mb is null || cpu.SocketId == 0 || mb.SocketId == 0)
        {
            return CompatibilityRuleOutcome.Fail(
                new CompatibilityIssue("MISSING_SPEC", "Thiếu thông số CPU/Mainboard để kiểm tra socket."));
        }

        if (cpu.SocketId == mb.SocketId)
        {
            return CompatibilityRuleOutcome.Ok(
                new CompatibilityMatch(
                    "SOCKET_MATCH",
                    $"CPU ({cpu.SocketCode}) phù hợp với Mainboard ({mb.SocketCode})."));
        }

        return CompatibilityRuleOutcome.Fail(
            new CompatibilityIssue(
                "SOCKET_MISMATCH",
                $"CPU dùng socket {cpu.SocketCode} còn Mainboard là {mb.SocketCode} — không lắp được vào nhau."));
    }
}

public sealed class MainboardRamStandardRule : ICompatibilityRule
{
    private readonly PcStoreDbContext _db;
    public MainboardRamStandardRule(PcStoreDbContext db) => _db = db;

    public async Task<CompatibilityRuleOutcome> EvaluateAsync(BuildSelection selection, CancellationToken ct = default)
    {
        if (selection.MainboardId is null || selection.RamId is null)
            return CompatibilityRuleOutcome.Empty;

        var mb = await _db.MainboardSpecs.AsNoTracking()
            .Where(x => x.ComponentId == selection.MainboardId.Value)
            .Select(x => new { x.RamStandardId, Code = x.RamStandard!.Code })
            .SingleOrDefaultAsync(ct);

        var ram = await _db.RamSpecs.AsNoTracking()
            .Where(x => x.ComponentId == selection.RamId.Value)
            .Select(x => new { x.RamStandardId, Code = x.RamStandard!.Code })
            .SingleOrDefaultAsync(ct);

        if (mb is null || ram is null || mb.RamStandardId == 0 || ram.RamStandardId == 0)
        {
            return CompatibilityRuleOutcome.Fail(
                new CompatibilityIssue("MISSING_SPEC", "Thiếu thông số RAM/Mainboard để kiểm tra DDR."));
        }

        if (mb.RamStandardId == ram.RamStandardId)
        {
            return CompatibilityRuleOutcome.Ok(
                new CompatibilityMatch(
                    "RAM_STANDARD_MATCH",
                    $"RAM {ram.Code} khớp chuẩn bộ nhớ của Mainboard ({mb.Code})."));
        }

        return CompatibilityRuleOutcome.Fail(
            new CompatibilityIssue(
                "RAM_STANDARD_MISMATCH",
                $"Mainboard hỗ trợ {mb.Code} nhưng RAM là {ram.Code} — không tương thích."));
    }
}

public sealed class RamKitQuantityRule : ICompatibilityRule
{
    private readonly PcStoreDbContext _db;
    public RamKitQuantityRule(PcStoreDbContext db) => _db = db;

    public async Task<CompatibilityRuleOutcome> EvaluateAsync(BuildSelection selection, CancellationToken ct = default)
    {
        if (selection.RamId is null)
            return CompatibilityRuleOutcome.Empty;

        var moduleCount = await _db.RamSpecs.AsNoTracking()
            .Where(x => x.ComponentId == selection.RamId.Value)
            .Select(x => x.ModuleCount)
            .SingleOrDefaultAsync(ct);

        if (moduleCount <= 0) moduleCount = 1;

        if (moduleCount >= 2 && selection.RamQty > 1)
        {
            return CompatibilityRuleOutcome.Fail(
                new CompatibilityIssue(
                    "RAM_KIT_QTY",
                    $"RAM này là kit {moduleCount} thanh (1 SKU). Nên để số lượng = 1, hiện đang chọn {selection.RamQty}."));
        }

        if (moduleCount >= 2)
        {
            return CompatibilityRuleOutcome.Ok(
                new CompatibilityMatch(
                    "RAM_KIT_OK",
                    $"RAM kit {moduleCount} thanh — số lượng 1 là đúng."));
        }

        return CompatibilityRuleOutcome.Empty;
    }
}

public sealed class PsuCapacityRule : ICompatibilityRule
{
    private readonly PcStoreDbContext _db;
    public PsuCapacityRule(PcStoreDbContext db) => _db = db;

    public async Task<CompatibilityRuleOutcome> EvaluateAsync(BuildSelection selection, CancellationToken ct = default)
    {
        if (selection.PsuId is null)
            return CompatibilityRuleOutcome.Empty;

        var psuCapacity = await _db.PsuSpecs.AsNoTracking()
            .Where(x => x.ComponentId == selection.PsuId.Value)
            .Select(x => x.CapacityWatt)
            .SingleOrDefaultAsync(ct);

        if (psuCapacity == 0)
        {
            return CompatibilityRuleOutcome.Fail(
                new CompatibilityIssue("MISSING_SPEC", "Thiếu thông số PSU để kiểm tra công suất."));
        }

        var cpuTdp = selection.CpuId is null ? 0 : await _db.CpuSpecs.AsNoTracking()
            .Where(x => x.ComponentId == selection.CpuId.Value)
            .Select(x => x.TdpWatt)
            .SingleOrDefaultAsync(ct);

        var gpuTdp = selection.GpuId is null ? 0 : await _db.GpuSpecs.AsNoTracking()
            .Where(x => x.ComponentId == selection.GpuId.Value)
            .Select(x => x.TdpWatt)
            .SingleOrDefaultAsync(ct);

        if (selection.CpuId is null && selection.GpuId is null)
            return CompatibilityRuleOutcome.Empty;

        var required = PsuPowerEstimator.EstimateRequiredWatt(
            cpuTdp,
            gpuTdp,
            hasSsd: selection.SsdId.HasValue,
            hasCase: selection.CaseId.HasValue);

        if (psuCapacity >= required)
        {
            return CompatibilityRuleOutcome.Ok(
                new CompatibilityMatch(
                    "PSU_SUFFICIENT",
                    $"Nguồn {psuCapacity}W đủ cho cấu hình (ước tính cần >= {required}W)."));
        }

        return CompatibilityRuleOutcome.Fail(
            new CompatibilityIssue(
                "PSU_INSUFFICIENT",
                $"Nguồn {psuCapacity}W có thể thiếu (ước tính cần >= {required}W) — máy dễ restart/không ổn định tải cao."));
    }
}
