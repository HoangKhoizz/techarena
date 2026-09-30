using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/pos")]
public sealed class PosCatalogController : ControllerBase
{
    private readonly PcStoreDbContext _db;

    public PosCatalogController(PcStoreDbContext db) => _db = db;

    /// <summary>Tìm kiếm nhanh linh kiện cho màn POS.</summary>
    [HttpGet("products")]
    public async Task<IActionResult> SearchProducts([FromQuery] string? q, CancellationToken ct)
    {
        var query = _db.Components.AsNoTracking()
            .Include(x => x.Brand)
            .Include(x => x.Category)
            .Where(x => x.IsActive && x.StockQty > 0);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var key = q.Trim();
            query = query.Where(x =>
                x.Name.Contains(key) ||
                x.Sku.Contains(key) ||
                (x.Brand != null && x.Brand.Name.Contains(key)) ||
                (x.Category != null && x.Category.DisplayName.Contains(key)));
        }

        var items = await query
            .OrderBy(x => x.Name)
            .Take(60)
            .Select(x => new
            {
                componentId = x.ComponentId,
                sku = x.Sku,
                name = x.Name,
                priceVnd = x.PriceVnd,
                stockQty = x.StockQty,
                brand = x.Brand != null ? x.Brand.Name : null,
                category = x.Category != null ? x.Category.DisplayName : null
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>Lookup khách theo SĐT để tự điền tên trên POS.</summary>
    [HttpGet("customer-by-phone")]
    public async Task<IActionResult> FindCustomerByPhone([FromQuery] string? phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return Ok(new { found = false });

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 9)
            return Ok(new { found = false });

        var customer = await _db.Customers.AsNoTracking()
            .Where(x => x.IsActive && x.Phone != null &&
                        (x.Phone == digits || x.Phone == phone.Trim() || x.Phone.EndsWith(digits)))
            .OrderByDescending(x => x.CustomerId)
            .Select(x => new
            {
                x.CustomerId,
                x.FullName,
                x.Username,
                x.Email,
                x.Phone
            })
            .FirstOrDefaultAsync(ct);

        if (customer is not null)
            return Ok(new { found = true, customer.CustomerId, fullName = customer.FullName, customer.Username, customer.Email, customer.Phone });

        // Fallback: đơn cũ cùng SĐT
        var fromOrder = await _db.Orders.AsNoTracking()
            .Where(x => x.ReceiverPhone == digits || x.ReceiverPhone == phone.Trim())
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.ReceiverName, x.ReceiverPhone, x.CustomerId })
            .FirstOrDefaultAsync(ct);

        if (fromOrder is null)
            return Ok(new { found = false });

        return Ok(new
        {
            found = true,
            customerId = fromOrder.CustomerId,
            fullName = fromOrder.ReceiverName,
            phone = fromOrder.ReceiverPhone,
            fromOrderHistory = true
        });
    }
}
