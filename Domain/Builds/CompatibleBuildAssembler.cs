using ban_link_kien_PC.Domain.Factories;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Builds;

public sealed record AssembledPart(string CategoryCode, int ComponentId, int Qty);

/// <summary>
/// Builds a compatible part stack (socket → DDR → PSU) for presets and ecosystems.
/// </summary>
public sealed class CompatibleBuildAssembler
{
    private readonly PcStoreDbContext _db;

    public CompatibleBuildAssembler(PcStoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<AssembledPart>> AssemblePresetAsync(
        string presetCode,
        IReadOnlyList<string> categories,
        CancellationToken ct = default)
    {
        var profile = PresetProfile.For(presetCode);
        var wantsGpu = categories.Any(c => c.Equals("GPU", StringComparison.OrdinalIgnoreCase));

        var best = await TryAssembleForSocketAsync(profile.PreferredSocket, profile, wantsGpu, preferredBrand: null, ct);
        if (best.Count == 0 && profile.FallbackSocket is not null)
            best = await TryAssembleForSocketAsync(profile.FallbackSocket, profile, wantsGpu, preferredBrand: null, ct);

        return FilterToCategories(best, categories);
    }

    public async Task<IReadOnlyList<AssembledPart>> AssembleEcosystemAsync(
        IEcosystemFactory eco,
        CancellationToken ct = default)
    {
        var categories = eco.CategoryRules.Select(r => r.CategoryCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var wantsGpu = categories.Any(c => c.Equals("GPU", StringComparison.OrdinalIgnoreCase));
        var wantsCpu = categories.Any(c => c.Equals("CPU", StringComparison.OrdinalIgnoreCase));

        // AMD ecosystem: lock to AM5. Brand ecosystems: prefer brand on MB/GPU/PSU/CASE, socket from CPU or MB.
        string? preferredSocket = eco.Code.Equals("AMD_PERF", StringComparison.OrdinalIgnoreCase) ? "AM5" : null;
        var profile = PresetProfile.For("GAMING");

        if (preferredSocket is null)
        {
            // Pick a branded motherboard first to determine socket, else cheap active CPU socket.
            var brandedMb = await (
                from c in _db.Components.AsNoTracking()
                join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
                join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
                from b in bj.DefaultIfEmpty()
                join mb in _db.MainboardSpecs.AsNoTracking() on c.ComponentId equals mb.ComponentId
                join sock in _db.Sockets.AsNoTracking() on mb.SocketId equals sock.SocketId
                where c.IsActive && cat.Code == "MAINBOARD" && b != null && b.Name == eco.PreferredBrand
                orderby c.PriceVnd
                select sock.Code).FirstOrDefaultAsync(ct);

            preferredSocket = brandedMb
                ?? await (
                    from c in _db.Components.AsNoTracking()
                    join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
                    join cpu in _db.CpuSpecs.AsNoTracking() on c.ComponentId equals cpu.ComponentId
                    join sock in _db.Sockets.AsNoTracking() on cpu.SocketId equals sock.SocketId
                    where c.IsActive && cat.Code == "CPU" && sock.Code != "AM4"
                    orderby c.PriceVnd
                    select sock.Code).FirstOrDefaultAsync(ct)
                ?? "AM5";
        }

        var stack = await TryAssembleForSocketAsync(
            preferredSocket,
            profile,
            wantsGpu,
            preferredBrand: eco.PreferredBrand,
            ct,
            requireCpu: wantsCpu);

        // Ensure categories requested by ecosystem exist (MONITOR etc. not in core stack).
        var byCat = stack.ToDictionary(x => x.CategoryCode, x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in eco.CategoryRules)
        {
            if (byCat.ContainsKey(rule.CategoryCode)) continue;
            var part = await PickCheapestAsync(rule.CategoryCode, eco.PreferredBrand, ct);
            if (part is not null)
                byCat[rule.CategoryCode] = new AssembledPart(rule.CategoryCode, part.Value, Math.Max(1, rule.Qty));
        }

        return eco.CategoryRules
            .Select(r => byCat.TryGetValue(r.CategoryCode, out var p) ? p : null)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<int> ResolveRamQtyAsync(int ramComponentId, int requestedQty, CancellationToken ct = default)
    {
        var moduleCount = await _db.RamSpecs.AsNoTracking()
            .Where(x => x.ComponentId == ramComponentId)
            .Select(x => x.ModuleCount)
            .FirstOrDefaultAsync(ct);

        if (moduleCount <= 0) moduleCount = 1;
        if (moduleCount >= 2)
            return 1; // kit already includes multiple sticks
        // Single sticks: default dual-channel (2) when caller sends 0/1.
        return requestedQty <= 1 ? 2 : requestedQty;
    }

    private async Task<List<AssembledPart>> TryAssembleForSocketAsync(
        string socketCode,
        PresetProfile profile,
        bool wantsGpu,
        string? preferredBrand,
        CancellationToken ct,
        bool requireCpu = true)
    {
        var result = new List<AssembledPart>();

        var cpu = await PickCpuAsync(socketCode, profile.CpuMaxPriceVnd, preferredBrand: null, ct);
        if (cpu is null && requireCpu) return result;
        if (cpu is not null)
            result.Add(new AssembledPart("CPU", cpu.ComponentId, 1));

        var socketId = cpu?.SocketId
            ?? await _db.Sockets.AsNoTracking().Where(s => s.Code == socketCode).Select(s => s.SocketId).FirstOrDefaultAsync(ct);
        if (socketId == 0) return result;

        var preferDdr5 = !socketCode.Equals("AM4", StringComparison.OrdinalIgnoreCase)
                         && (profile.PreferDdr5 || socketCode.Equals("AM5", StringComparison.OrdinalIgnoreCase));

        var mb = await PickMainboardAsync(socketId, preferDdr5, profile.MbMaxPriceVnd, preferredBrand, ct);
        if (mb is null)
            mb = await PickMainboardAsync(socketId, preferDdr5: false, profile.MbMaxPriceVnd * 2, preferredBrand, ct);
        if (mb is null) return result;
        result.Add(new AssembledPart("MAINBOARD", mb.ComponentId, 1));

        var ram = await PickRamAsync(mb.RamStandardId, profile.PreferRamKit, profile.RamMaxPriceVnd, ct);
        if (ram is not null)
        {
            var qty = ram.ModuleCount >= 2 ? 1 : 2;
            result.Add(new AssembledPart("RAM", ram.ComponentId, qty));
        }

        int gpuTdp = 0;
        if (wantsGpu)
        {
            var gpu = await PickGpuAsync(profile.GpuMinPriceVnd, profile.GpuMaxPriceVnd, preferredBrand, ct);
            if (gpu is not null)
            {
                result.Add(new AssembledPart("GPU", gpu.ComponentId, 1));
                gpuTdp = gpu.TdpWatt;
            }
        }

        var cpuTdp = cpu?.TdpWatt ?? 65;
        var requiredWatt = PsuPowerEstimator.EstimateRequiredWatt(cpuTdp, gpuTdp, hasSsd: true, hasCase: true);
        var psu = await PickPsuAsync(requiredWatt, preferredBrand, ct);
        if (psu is not null)
            result.Add(new AssembledPart("PSU", psu.Value, 1));

        return result;
    }

    private static IReadOnlyList<AssembledPart> FilterToCategories(
        IReadOnlyList<AssembledPart> core,
        IReadOnlyList<string> categories)
    {
        var map = core.ToDictionary(x => x.CategoryCode, x => x, StringComparer.OrdinalIgnoreCase);
        var list = new List<AssembledPart>();
        foreach (var cat in categories)
        {
            if (map.TryGetValue(cat, out var part))
            {
                list.Add(part);
                continue;
            }

            // SSD / CASE / MONITOR / HEADSET: filled later by caller via cheapest pick
        }

        return list;
    }

    public async Task<AssembledPart?> PickAccessoryAsync(string categoryCode, string? preferredBrand, CancellationToken ct)
    {
        var id = await PickCheapestAsync(categoryCode, preferredBrand, ct);
        return id is null ? null : new AssembledPart(categoryCode, id.Value, 1);
    }

    private async Task<CpuPick?> PickCpuAsync(string socketCode, decimal maxPrice, string? preferredBrand, CancellationToken ct)
    {
        var q =
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join cpu in _db.CpuSpecs.AsNoTracking() on c.ComponentId equals cpu.ComponentId
            join sock in _db.Sockets.AsNoTracking() on cpu.SocketId equals sock.SocketId
            join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
            from b in bj.DefaultIfEmpty()
            where c.IsActive && cat.Code == "CPU" && sock.Code == socketCode && c.PriceVnd <= maxPrice
            orderby (preferredBrand != null && b != null && b.Name == preferredBrand) descending, c.PriceVnd
            select new CpuPick(c.ComponentId, cpu.SocketId, cpu.TdpWatt, c.PriceVnd);

        var pick = await q.FirstOrDefaultAsync(ct);
        if (pick is not null) return pick;

        // Relax price cap
        return await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join cpu in _db.CpuSpecs.AsNoTracking() on c.ComponentId equals cpu.ComponentId
            join sock in _db.Sockets.AsNoTracking() on cpu.SocketId equals sock.SocketId
            where c.IsActive && cat.Code == "CPU" && sock.Code == socketCode
            orderby c.PriceVnd
            select new CpuPick(c.ComponentId, cpu.SocketId, cpu.TdpWatt, c.PriceVnd)
        ).FirstOrDefaultAsync(ct);
    }

    private async Task<MbPick?> PickMainboardAsync(
        int socketId,
        bool preferDdr5,
        decimal maxPrice,
        string? preferredBrand,
        CancellationToken ct)
    {
        var ddr5Id = await _db.RamStandards.AsNoTracking()
            .Where(x => x.Code == "DDR5")
            .Select(x => x.RamStandardId)
            .FirstOrDefaultAsync(ct);

        var q =
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join mb in _db.MainboardSpecs.AsNoTracking() on c.ComponentId equals mb.ComponentId
            join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
            from b in bj.DefaultIfEmpty()
            where c.IsActive && cat.Code == "MAINBOARD" && mb.SocketId == socketId && c.PriceVnd <= maxPrice
            orderby
                (preferDdr5 && ddr5Id != 0 && mb.RamStandardId == ddr5Id) descending,
                (preferredBrand != null && b != null && b.Name == preferredBrand) descending,
                c.PriceVnd
            select new MbPick(c.ComponentId, mb.RamStandardId, c.PriceVnd);

        var pick = await q.FirstOrDefaultAsync(ct);
        if (pick is not null) return pick;

        return await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join mb in _db.MainboardSpecs.AsNoTracking() on c.ComponentId equals mb.ComponentId
            join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
            from b in bj.DefaultIfEmpty()
            where c.IsActive && cat.Code == "MAINBOARD" && mb.SocketId == socketId
            orderby (preferredBrand != null && b != null && b.Name == preferredBrand) descending, c.PriceVnd
            select new MbPick(c.ComponentId, mb.RamStandardId, c.PriceVnd)
        ).FirstOrDefaultAsync(ct);
    }

    private async Task<RamPick?> PickRamAsync(int ramStandardId, bool preferKit, decimal maxPrice, CancellationToken ct)
    {
        var q =
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join ram in _db.RamSpecs.AsNoTracking() on c.ComponentId equals ram.ComponentId
            where c.IsActive && cat.Code == "RAM" && ram.RamStandardId == ramStandardId && c.PriceVnd <= maxPrice
            orderby (preferKit && ram.ModuleCount >= 2) descending, ram.CapacityGb descending, c.PriceVnd
            select new RamPick(c.ComponentId, ram.ModuleCount <= 0 ? 1 : ram.ModuleCount, c.PriceVnd);

        var pick = await q.FirstOrDefaultAsync(ct);
        if (pick is not null) return pick;

        return await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join ram in _db.RamSpecs.AsNoTracking() on c.ComponentId equals ram.ComponentId
            where c.IsActive && cat.Code == "RAM" && ram.RamStandardId == ramStandardId
            orderby (preferKit && ram.ModuleCount >= 2) descending, c.PriceVnd
            select new RamPick(c.ComponentId, ram.ModuleCount <= 0 ? 1 : ram.ModuleCount, c.PriceVnd)
        ).FirstOrDefaultAsync(ct);
    }

    private async Task<GpuPick?> PickGpuAsync(decimal minPrice, decimal maxPrice, string? preferredBrand, CancellationToken ct)
    {
        var q =
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join gpu in _db.GpuSpecs.AsNoTracking() on c.ComponentId equals gpu.ComponentId
            join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
            from b in bj.DefaultIfEmpty()
            where c.IsActive && cat.Code == "GPU" && c.PriceVnd >= minPrice && c.PriceVnd <= maxPrice
            orderby (preferredBrand != null && b != null && b.Name == preferredBrand) descending, c.PriceVnd
            select new GpuPick(c.ComponentId, gpu.TdpWatt, c.PriceVnd);

        var pick = await q.FirstOrDefaultAsync(ct);
        if (pick is not null) return pick;

        // Closest under max, else cheapest overall
        pick = await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join gpu in _db.GpuSpecs.AsNoTracking() on c.ComponentId equals gpu.ComponentId
            where c.IsActive && cat.Code == "GPU" && c.PriceVnd <= maxPrice
            orderby c.PriceVnd descending
            select new GpuPick(c.ComponentId, gpu.TdpWatt, c.PriceVnd)
        ).FirstOrDefaultAsync(ct);

        return pick ?? await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join gpu in _db.GpuSpecs.AsNoTracking() on c.ComponentId equals gpu.ComponentId
            where c.IsActive && cat.Code == "GPU"
            orderby c.PriceVnd
            select new GpuPick(c.ComponentId, gpu.TdpWatt, c.PriceVnd)
        ).FirstOrDefaultAsync(ct);
    }

    private async Task<int?> PickPsuAsync(int requiredWatt, string? preferredBrand, CancellationToken ct)
    {
        var q =
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join psu in _db.PsuSpecs.AsNoTracking() on c.ComponentId equals psu.ComponentId
            join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
            from b in bj.DefaultIfEmpty()
            where c.IsActive && cat.Code == "PSU" && psu.CapacityWatt >= requiredWatt
            orderby (preferredBrand != null && b != null && b.Name == preferredBrand) descending, c.PriceVnd
            select c.ComponentId;

        var id = await q.FirstOrDefaultAsync(ct);
        if (id != 0) return id;

        return await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join psu in _db.PsuSpecs.AsNoTracking() on c.ComponentId equals psu.ComponentId
            where c.IsActive && cat.Code == "PSU"
            orderby psu.CapacityWatt descending, c.PriceVnd
            select (int?)c.ComponentId
        ).FirstOrDefaultAsync(ct);
    }

    private async Task<int?> PickCheapestAsync(string categoryCode, string? preferredBrand, CancellationToken ct)
    {
        return await (
            from c in _db.Components.AsNoTracking()
            join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
            join b in _db.Brands.AsNoTracking() on c.BrandId equals b.BrandId into bj
            from b in bj.DefaultIfEmpty()
            where c.IsActive && cat.Code == categoryCode
            orderby (preferredBrand != null && b != null && b.Name == preferredBrand) descending, c.PriceVnd
            select (int?)c.ComponentId
        ).FirstOrDefaultAsync(ct);
    }

    private sealed record CpuPick(int ComponentId, int SocketId, int TdpWatt, decimal PriceVnd);
    private sealed record MbPick(int ComponentId, int RamStandardId, decimal PriceVnd);
    private sealed record RamPick(int ComponentId, int ModuleCount, decimal PriceVnd);
    private sealed record GpuPick(int ComponentId, int TdpWatt, decimal PriceVnd);

    private sealed class PresetProfile
    {
        public string PreferredSocket { get; init; } = "AM5";
        public string? FallbackSocket { get; init; } = "LGA1700";
        public decimal CpuMaxPriceVnd { get; init; } = 8_000_000m;
        public decimal MbMaxPriceVnd { get; init; } = 5_000_000m;
        public decimal RamMaxPriceVnd { get; init; } = 4_000_000m;
        public decimal GpuMinPriceVnd { get; init; } = 0m;
        public decimal GpuMaxPriceVnd { get; init; } = 15_000_000m;
        public bool PreferDdr5 { get; init; } = true;
        public bool PreferRamKit { get; init; } = true;

        public static PresetProfile For(string presetCode)
        {
            var code = (presetCode ?? "").Trim().ToUpperInvariant();
            return code switch
            {
                "BUDGET" or "OFFICE" => new PresetProfile
                {
                    PreferredSocket = "LGA1700",
                    FallbackSocket = "AM5",
                    CpuMaxPriceVnd = 5_500_000m,
                    MbMaxPriceVnd = 3_500_000m,
                    RamMaxPriceVnd = 2_500_000m,
                    GpuMinPriceVnd = 0m,
                    GpuMaxPriceVnd = 7_000_000m,
                    PreferDdr5 = false,
                    PreferRamKit = false
                },
                "WORKSTATION" => new PresetProfile
                {
                    PreferredSocket = "AM5",
                    FallbackSocket = "LGA1700",
                    CpuMaxPriceVnd = 20_000_000m,
                    MbMaxPriceVnd = 8_000_000m,
                    RamMaxPriceVnd = 5_000_000m,
                    GpuMinPriceVnd = 9_000_000m,
                    GpuMaxPriceVnd = 25_000_000m,
                    PreferDdr5 = true,
                    PreferRamKit = true
                },
                "STREAMING" => new PresetProfile
                {
                    PreferredSocket = "AM5",
                    FallbackSocket = "LGA1700",
                    CpuMaxPriceVnd = 12_000_000m,
                    MbMaxPriceVnd = 5_000_000m,
                    RamMaxPriceVnd = 4_000_000m,
                    GpuMinPriceVnd = 7_000_000m,
                    GpuMaxPriceVnd = 16_000_000m,
                    PreferDdr5 = true,
                    PreferRamKit = true
                },
                _ => new PresetProfile
                {
                    PreferredSocket = "AM5",
                    FallbackSocket = "LGA1700",
                    CpuMaxPriceVnd = 12_000_000m,
                    MbMaxPriceVnd = 5_000_000m,
                    RamMaxPriceVnd = 4_000_000m,
                    GpuMinPriceVnd = 6_000_000m,
                    GpuMaxPriceVnd = 15_000_000m,
                    PreferDdr5 = true,
                    PreferRamKit = true
                }
            };
        }
    }
}

public static class PsuPowerEstimator
{
    public static int EstimateRequiredWatt(int cpuTdp, int gpuTdp, bool hasSsd, bool hasCase)
    {
        var overhead = 100;
        if (hasSsd) overhead += 15;
        if (hasCase) overhead += 25; // fans
        overhead += 20; // MB + RAM baseline extras beyond CPU/GPU

        var estimated = cpuTdp + gpuTdp + overhead;
        var factor = gpuTdp >= 300 ? 1.30 : 1.25;
        return (int)Math.Ceiling(estimated * factor);
    }
}
