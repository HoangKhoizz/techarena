using ban_link_kien_PC.Domain.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController : ControllerBase
{
    private readonly IComponentCatalogQuery _query;
    public CatalogController(IComponentCatalogQuery query) => _query = query;

    // Example:
    // GET /api/catalog/search?categoryCode=CPU&socketCode=LGA1700
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<ComponentListItem>>> Search(
        [FromQuery] string? categoryCode,
        [FromQuery] string? socketCode,
        [FromQuery] string? chipset,
        [FromQuery] string? ramStandardCode,
        CancellationToken ct)
    {
        var items = await _query.SearchAsync(categoryCode, socketCode, chipset, ramStandardCode, ct);
        return Ok(items);
    }
}

