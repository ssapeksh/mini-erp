using Erp.Core.Common;
using Erp.Core.Entities;
using Erp.Core.Interfaces;
using Erp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Infrastructure.Persistence;

/// <summary>
/// Generic repository, registered once as an OPEN GENERIC:
/// services.AddScoped(typeof(IRepository&lt;&gt;), typeof(Repository&lt;&gt;)).
/// </summary>
internal sealed class Repository<T>(ErpDbContext db) : IRepository<T> where T : BaseEntity
{
    private readonly DbSet<T> _set = db.Set<T>();

    public IQueryable<T> Query() => _set;
    public IQueryable<T> QueryNoTracking() => _set.AsNoTracking();

    public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _set.FirstOrDefaultAsync(e => e.Id == id, ct);    // FindAsync would skip the soft-delete filter for cached entities

    public void Add(T entity) => _set.Add(entity);

    /// <summary>
    /// Soft delete by flag instead of DbSet.Remove: Remove would also make EF
    /// cascade-delete any loaded children (e.g. order lines) for real.
    /// </summary>
    public void Remove(T entity) => entity.IsDeleted = true;
}

internal sealed class UnitOfWork(ErpDbContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // UPDATE ... WHERE RowVersion = @original touched 0 rows
            throw new ConcurrencyException("This record was changed by someone else after you loaded it. Reload and try again.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql
                                           && SqlErrorTranslator.Translate(sql) is { } translated)   // exception filter
        {
            throw translated;
        }
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        new EfTransaction(await db.Database.BeginTransactionAsync(ct));

    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
        db.Entry(entity).Property("RowVersion").OriginalValue = rowVersion;

    public DbSet<TEntity> Set<TEntity>() where TEntity : class => db.Set<TEntity>();

    /// <summary>Adapter: hides EF's IDbContextTransaction behind the Core interface.</summary>
    private sealed class EfTransaction(IDbContextTransaction tx) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => tx.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => tx.RollbackAsync(ct);
        public ValueTask DisposeAsync() => tx.DisposeAsync();     // dispose without commit = rollback
    }
}
