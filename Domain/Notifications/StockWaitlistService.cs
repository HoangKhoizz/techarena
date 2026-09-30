using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Notifications;

// Observer pattern: customers "subscribe" to stock changes.
public interface IStockObserver
{
    Task OnBackInStockAsync(int componentId, IReadOnlyList<string> customerEmails, CancellationToken ct = default);
}

public interface IStockWaitlistService
{
    Task AddToWaitlistAsync(int componentId, int customerId, CancellationToken ct = default);
    Task<bool> RemoveFromWaitlistAsync(int componentId, int customerId, CancellationToken ct = default);
    Task<bool> IsInWaitlistAsync(int componentId, int customerId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> NotifyBackInStockAsync(int componentId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ProcessRestockAsync(int componentId, int newStockQty, CancellationToken ct = default);
    void RegisterObserver(IStockObserver observer);
}

public sealed class StockWaitlistService : IStockWaitlistService
{
    private readonly PcStoreDbContext _db;
    private readonly List<IStockObserver> _observers = [];

    public StockWaitlistService(PcStoreDbContext db) => _db = db;

    public void RegisterObserver(IStockObserver observer) => _observers.Add(observer);

    public async Task AddToWaitlistAsync(int componentId, int customerId, CancellationToken ct = default)
    {
        var customerExists = await _db.Customers.AsNoTracking()
            .AnyAsync(x => x.CustomerId == customerId, ct);
        if (!customerExists)
            return;

        var componentExists = await _db.Components.AsNoTracking()
            .AnyAsync(x => x.ComponentId == componentId, ct);
        if (!componentExists)
            return;

        var exists = await _db.StockWaitlists.AsNoTracking()
            .AnyAsync(x => x.ComponentId == componentId && x.CustomerId == customerId, ct);
        if (exists) return;

        _db.StockWaitlists.Add(new StockWaitlistEntity
        {
            ComponentId = componentId,
            CustomerId = customerId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveFromWaitlistAsync(int componentId, int customerId, CancellationToken ct = default)
    {
        var row = await _db.StockWaitlists
            .FirstOrDefaultAsync(x => x.ComponentId == componentId && x.CustomerId == customerId, ct);
        if (row is null)
            return false;

        _db.StockWaitlists.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> IsInWaitlistAsync(int componentId, int customerId, CancellationToken ct = default)
        => _db.StockWaitlists.AsNoTracking()
            .AnyAsync(x => x.ComponentId == componentId && x.CustomerId == customerId, ct);

    public async Task<IReadOnlyList<string>> NotifyBackInStockAsync(int componentId, CancellationToken ct = default)
    {
        var customerEmails = await (from w in _db.StockWaitlists.AsNoTracking()
                                    join c in _db.Customers.AsNoTracking() on w.CustomerId equals c.CustomerId
                                    where w.ComponentId == componentId
                                    select c.Email)
            .Distinct()
            .ToListAsync(ct);

        foreach (var obs in _observers)
            await obs.OnBackInStockAsync(componentId, customerEmails, ct);

        return customerEmails;
    }

    public async Task<IReadOnlyList<string>> ProcessRestockAsync(int componentId, int newStockQty, CancellationToken ct = default)
    {
        if (newStockQty <= 0)
            return [];

        var emails = await NotifyBackInStockAsync(componentId, ct);
        if (emails.Count == 0)
            return emails;

        var waitRows = await _db.StockWaitlists
            .Where(x => x.ComponentId == componentId)
            .ToListAsync(ct);
        if (waitRows.Count > 0)
        {
            _db.StockWaitlists.RemoveRange(waitRows);
            await _db.SaveChangesAsync(ct);
        }
        return emails;
    }
}

public sealed class InAppStockObserver : IStockObserver
{
    public Task OnBackInStockAsync(int componentId, IReadOnlyList<string> customerEmails, CancellationToken ct = default)
    {
        // Demo observer: hook point for email/sms integration later.
        return Task.CompletedTask;
    }
}

