using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MiniErp.Contracts;

namespace Erp.Core.Common;

/// <summary>
/// EXTENSION METHODS on IQueryable&lt;T&gt;. Because they extend IQueryable (not
/// IEnumerable), the filter/sort/paging become part of the SQL that EF Core
/// sends; nothing is filtered in memory.
/// </summary>
public static class QueryableExtensions
{
    public const int MaxPageSize = 100;

    /// <summary>Runs two queries: COUNT(*) and one page with OFFSET/FETCH.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);         // never trust the client's page size

        int total = await query.CountAsync(ct);
        if (total == 0) return PagedResult<T>.Empty(page, pageSize);

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, pageSize);
    }

    /// <summary>
    /// Sort by a property name chosen at run time (e.g. "?sortBy=name") WITHOUT
    /// string-built SQL. It builds an EXPRESSION TREE equivalent to
    /// <c>x =&gt; x.Name</c>, which EF Core translates to ORDER BY Name.
    /// Unknown names are ignored, so user input can never become SQL.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(this IQueryable<T> query, string? sortBy, bool descending)
    {
        if (string.IsNullOrWhiteSpace(sortBy)) return query;

        PropertyInfo? prop = typeof(T).GetProperty(sortBy,
            BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
        if (prop is null) return query;

        ParameterExpression x = Expression.Parameter(typeof(T), "x");          // x
        MemberExpression body = Expression.Property(x, prop);                    // x.Name
        LambdaExpression keySelector = Expression.Lambda(body, x);               // x => x.Name

        string method = descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        MethodCallExpression call = Expression.Call(
            typeof(Queryable), method, [typeof(T), prop.PropertyType],
            query.Expression, Expression.Quote(keySelector));

        return query.Provider.CreateQuery<T>(call);
    }

    /// <summary>Apply a filter only when a condition is true: keeps optional filters readable.</summary>
    public static IQueryable<T> WhereIf<T>(this IQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
        => condition ? query.Where(predicate) : query;
}
