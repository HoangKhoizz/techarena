using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Infrastructure.Persistence;

public interface IRepository<T> where T : class
{
    IQueryable<T> Query();
    ValueTask<T?> FindAsync(params object[] keyValues);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Remove(T entity);
}

public sealed class EfRepository<T> : IRepository<T> where T : class
{
    private readonly PcStoreDbContext _db;
    public EfRepository(PcStoreDbContext db) => _db = db;

    public IQueryable<T> Query() => _db.Set<T>().AsQueryable();
    public ValueTask<T?> FindAsync(params object[] keyValues) => _db.Set<T>().FindAsync(keyValues);
    public Task AddAsync(T entity, CancellationToken ct = default) => _db.Set<T>().AddAsync(entity, ct).AsTask();
    public void Remove(T entity) => _db.Set<T>().Remove(entity);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly PcStoreDbContext _db;
    public EfUnitOfWork(PcStoreDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

