using Erp.Core.Dtos;
using Erp.Core.Entities;
using MiniErp.Contracts;

namespace Erp.Core.Interfaces;

/// <summary>The logged-in user, read from the JWT claims (implemented in Erp.Api).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int UserId { get; }
    string UserName { get; }
    Guid? SessionId { get; }
    IReadOnlyCollection<string> Permissions { get; }
    bool HasPermission(string permissionCode);
}

/*  READ SIDE (CQRS-style): queries go straight to SQL with Dapper (views,
    stored procedures, window functions) and return DTOs. The WRITE SIDE goes
    through EF Core entities, repositories and the unit of work.             */

public interface IItemQueries
{
    Task<PagedResult<ItemListItemDto>> GetPagedAsync(ItemQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<ItemStockDto>> GetStockAsync(int itemId, CancellationToken ct = default);
    Task<IReadOnlyList<ItemHistoryDto>> GetHistoryAsync(int itemId, CancellationToken ct = default);
    IAsyncEnumerable<ItemDto> StreamAllAsync(CancellationToken ct = default);
}

public interface IOrderQueries
{
    Task<PagedResult<OrderSummaryDto>> GetPagedAsync(OrderQuery query, CancellationToken ct = default);
}

public interface IStockQueries
{
    Task<IReadOnlyList<StockSummaryDto>> GetSummaryAsync(int? warehouseId, string? status, CancellationToken ct = default);
    Task<IReadOnlyList<RunningStockDto>> GetRunningStockAsync(int itemId, int? warehouseId, CancellationToken ct = default);
}

public interface IReportQueries
{
    Task<IReadOnlyList<MonthlySalesDto>> GetMonthlySalesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LatestOrderDto>> GetLatestOrdersAsync(int topN, bool includeCustomersWithoutOrders, CancellationToken ct = default);
    Task<IReadOnlyList<StatusCountDto>> GetOrderStatusCountsAsync(CancellationToken ct = default);
    Task<int> GetLowStockCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TopItemDto>> GetTopItemsAsync(int topN, CancellationToken ct = default);
}

/// <summary>Stored procedures that own a whole transaction (Phase 1 SQL).</summary>
public interface IOrderProcedures
{
    Task<string> GetNextOrderNoAsync(DateOnly orderDate, CancellationToken ct = default);
    Task<DespatchResultDto> DespatchAsync(int orderId, string despatchedBy, CancellationToken ct = default);
}

/// <summary>Cached master lists that rarely change.</summary>
public interface ILookupCache
{
    ValueTask<IReadOnlyList<LookupDto>> GetCategoriesAsync(CancellationToken ct = default);
    ValueTask<IReadOnlyList<LookupDto>> GetWarehousesAsync(CancellationToken ct = default);
    void Invalidate();
}

/// <summary>
/// Tax rate for an item. Phase 2: from the item master. Phase 5 swaps in a
/// CoreWCF pricing service: same interface, so no business code changes
/// (Open/Closed + Dependency Inversion).
/// </summary>
public interface ITaxRateProvider
{
    ValueTask<decimal> GetTaxPercentAsync(Item item, CancellationToken ct = default);
}
