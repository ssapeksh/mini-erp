using Dapper;
using Erp.Core.Dtos;
using Erp.Core.Interfaces;
using Erp.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;

namespace Erp.Infrastructure.Caching;

/// <summary>
/// SINGLETON cache for master lists that rarely change (categories, warehouses).
/// THREAD SAFETY on display:
///   - IMemoryCache is thread-safe by itself
///   - SemaphoreSlim(1,1) prevents a CACHE STAMPEDE: when the entry expires and
///     50 requests arrive together, only one hits the database; the rest wait
///     asynchronously (lock can't be used across 'await', SemaphoreSlim can)
///   - Interlocked counts hits/misses without a lock
/// A singleton must not depend on scoped services (no DbContext here):
/// it uses the singleton SqlConnectionFactory + Dapper.
/// </summary>
public sealed class LookupCache(SqlConnectionFactory factory, IMemoryCache cache) : ILookupCache
{
    private const string CategoriesKey = "lookup:categories";
    private const string WarehousesKey = "lookup:warehouses";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _hits, _misses;

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);

    public ValueTask<IReadOnlyList<LookupDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        GetAsync(CategoriesKey,
            "SELECT Id, CAST(Id AS VARCHAR(10)) AS Code, Name FROM dbo.ItemCategories WHERE IsDeleted = 0 ORDER BY Name;", ct);

    public ValueTask<IReadOnlyList<LookupDto>> GetWarehousesAsync(CancellationToken ct = default) =>
        GetAsync(WarehousesKey,
            "SELECT Id, Code, Name FROM dbo.Warehouses WHERE IsDeleted = 0 ORDER BY Code;", ct);

    public void Invalidate()
    {
        cache.Remove(CategoriesKey);
        cache.Remove(WarehousesKey);
    }

    /// <summary>
    /// ValueTask: on a cache hit the result is returned synchronously with no
    /// Task allocation; only a miss goes async.
    /// </summary>
    private ValueTask<IReadOnlyList<LookupDto>> GetAsync(string key, string sql, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out IReadOnlyList<LookupDto>? cached) && cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return ValueTask.FromResult(cached);
        }
        return new ValueTask<IReadOnlyList<LookupDto>>(LoadAsync(key, sql, ct));
    }

    private async Task<IReadOnlyList<LookupDto>> LoadAsync(string key, string sql, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // double-check: another request may have filled it while we waited
            if (cache.TryGetValue(key, out IReadOnlyList<LookupDto>? cached) && cached is not null)
                return cached;

            Interlocked.Increment(ref _misses);
            await using var conn = await factory.OpenAsync(ct);
            var rows = (await conn.QueryAsync<LookupDto>(new CommandDefinition(sql, cancellationToken: ct))).AsList();
            cache.Set(key, (IReadOnlyList<LookupDto>)rows, Ttl);
            return rows;
        }
        finally
        {
            _gate.Release();      // always release, even on exceptions
        }
    }
}
