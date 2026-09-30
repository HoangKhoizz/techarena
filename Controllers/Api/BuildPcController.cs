using ban_link_kien_PC.Domain.Builds;
using ban_link_kien_PC.Domain.Compatibility;
using Microsoft.AspNetCore.Mvc;

namespace ban_link_kien_PC.Controllers.Api;

public sealed record BuildPcRequest(
    int? CpuId,
    int? MainboardId,
    int? RamId,
    int RamQty,
    int? GpuId,
    int? PsuId);

[ApiController]
[Route("api/build")]
public sealed class BuildPcController : ControllerBase
{
    private readonly BuildPcFacade _build;
    public BuildPcController(BuildPcFacade build) => _build = build;

    [HttpPost("validate")]
    public async Task<ActionResult<object>> Validate([FromBody] BuildPcRequest req, CancellationToken ct)
    {
        var selection = new BuildSelection(
            req.CpuId,
            req.MainboardId,
            req.RamId,
            Math.Max(1, req.RamQty),
            req.GpuId,
            req.PsuId);
        var result = await _build.ValidateAsync(selection, ct);
        var total = await _build.CalculateTotalVndAsync(selection, ct);
        return Ok(new { result.IsValid, result.Issues, result.Matches, TotalPriceVnd = total });
    }
}

