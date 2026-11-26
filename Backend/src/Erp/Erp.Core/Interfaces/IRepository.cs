using Erp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Erp.Core.Interfaces;

/// <summary>
/// GENERIC REPOSITORY with a CONSTRAINT: T must be a BaseEntity, so every
/// repository can rely on Id and IsDeleted.
/// <see cref="Query"/> returns IQueryable: callers add Where/Select and EF
/// turns the whole chain into ONE SQL statement. Returning IEnumerable would
/// load every row first and filter in memory.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    /// <summary>Tracked query: for entities you are going to change.</summary>
    IQueryable<T> Query();

    /// <summary>AsNoTracking: faster read-only queries (no change-tracker snapshot).</summary>
    IQueryable<T> QueryNoTracking();

    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    void Add(T entity);

    /// <summary>Marks for deletion; the SaveChanges interceptor turns it into a soft delete.</summary>
    void Remove(T entity);
}

/// <summary>
/// UNIT OF WORK: all repository changes in one request are saved together by
/// one SaveChangesAsync call (one database transaction).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Explicit transaction when several SaveChanges or raw SQL calls must commit together.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Tells EF which RowVersion the client saw; UPDATE ... WHERE RowVersion = @original.</summary>
    void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class;

    /// <summary>Access to tables that aren't BaseEntity aggregates (stock balances, ledger, sessions).</summary>
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
