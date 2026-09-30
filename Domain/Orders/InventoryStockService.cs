using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Orders;

/// <summary>
/// Trừ/cộng tồn kho cho đơn ONLINE (và khi hủy mọi loại đơn đã trừ kho).
/// Rule: trừ ngay khi đặt hàng thành công (COD + MoMo WaitingPayment) để giữ chỗ.
/// Trừ kho dùng UPDATE atomic (WHERE StockQty &gt;= qty) để tránh bán vượt tồn khi concurrent.
/// </summary>
public sealed class InventoryStockService
{
    private readonly PcStoreDbContext _db;

    public InventoryStockService(PcStoreDbContext db) => _db = db;

    public const string InsufficientStockMessage = "Số lượng trong kho không đủ.";

    public async Task<(bool Ok, string? Error)> TryDeductAsync(
        IReadOnlyList<(int ComponentId, int Qty)> lines,
        CancellationToken ct = default)
    {
        if (lines.Count == 0)
            return (true, null);

        foreach (var group in lines.GroupBy(x => x.ComponentId))
        {
            var componentId = group.Key;
            var totalQty = group.Sum(x => x.Qty);
            if (totalQty <= 0)
                return (false, "Số lượng không hợp lệ.");

            var ok = await DeductOneAsync(componentId, totalQty, ct);
            if (!ok)
            {
                var info = await _db.Components.AsNoTracking()
                    .Where(x => x.ComponentId == componentId)
                    .Select(x => new { x.Name, x.StockQty })
                    .FirstOrDefaultAsync(ct);
                var name = info?.Name ?? $"#{componentId}";
                var remain = info?.StockQty ?? 0;
                return (false, $"{InsufficientStockMessage} «{name}» (còn {remain}, cần {totalQty}).");
            }
        }

        return (true, null);
    }

    private async Task<bool> DeductOneAsync(int componentId, int totalQty, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Component
SET StockQty = StockQty - {totalQty}
WHERE ComponentId = {componentId}
  AND IsActive = 1
  AND StockQty >= {totalQty}", ct);
            return affected == 1;
        }

        // Fallback InMemory / non-relational
        var c = await _db.Components.SingleOrDefaultAsync(x => x.ComponentId == componentId && x.IsActive, ct);
        if (c is null || c.StockQty < totalQty)
            return false;
        c.StockQty -= totalQty;
        return true;
    }

    /// <summary>Cộng lại kho theo OrderItem của đơn (chỉ gọi khi hủy / giao thất bại lần đầu).</summary>
    public async Task RestoreForOrderAsync(int orderId, CancellationToken ct = default)
    {
        var items = await _db.OrderItems.AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .Select(x => new { x.ComponentId, x.Qty })
            .ToListAsync(ct);

        if (items.Count == 0)
            return;

        foreach (var group in items.GroupBy(x => x.ComponentId))
        {
            var componentId = group.Key;
            var totalQty = group.Sum(x => x.Qty);
            if (totalQty <= 0) continue;

            if (_db.Database.IsRelational())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Component
SET StockQty = StockQty + {totalQty}
WHERE ComponentId = {componentId}", ct);
            }
            else
            {
                var c = await _db.Components.SingleOrDefaultAsync(x => x.ComponentId == componentId, ct);
                if (c is not null)
                    c.StockQty += totalQty;
            }
        }
    }
}
