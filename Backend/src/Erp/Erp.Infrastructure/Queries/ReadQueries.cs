using System.Data;
using System.Runtime.CompilerServices;
using Dapper;
using Erp.Core.Dtos;
using Erp.Core.Interfaces;
using Erp.Infrastructure.Data;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MiniErp.Contracts;

namespace Erp.Infrastructure.Queries;

/*  READ SIDE with Dapper: hand-written SQL / stored procedures from Phase 1,
    mapped straight to row classes, then to DTO records. Every value goes in
    as a parameter (@Search, @Skip...): no string concatenation, no injection.
    Row classes use plain types (DateTime, byte[]) that ADO.NET returns; the
    DTOs get the API-friendly types (DateOnly, base64 string).              */

internal sealed class ItemQueries(SqlConnectionFactory factory, ErpDbContext db) : IItemQueries
{
    public async Task<PagedResult<ItemListItemDto>> GetPagedAsync(ItemQuery q, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("@Search", q.Search, DbType.String, size: 100);
        p.Add("@CategoryId", q.CategoryId);
        p.Add("@SortBy", q.SortBy, DbType.AnsiString, size: 20);      // AnsiString = VARCHAR: no implicit conversion
        p.Add("@SortDir", q.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC", DbType.AnsiString, size: 4);
        p.Add("@PageNumber", q.Page);
        p.Add("@PageSize", q.PageSize);
        p.Add("@TotalCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ItemRow>(new CommandDefinition(
            "dbo.usp_GetItemsPaged", p, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var items = rows.Select(r => new ItemListItemDto(r.Id, r.Code, r.Name, r.CategoryId, r.CategoryName, r.Unit,
            r.UnitPrice, r.TaxPercent, r.ReorderLevel, r.IsActive, r.StockQty, r.IsBelowReorder,
            Convert.ToBase64String(r.RowVersion))).ToList();

        int total = p.Get<int?>("@TotalCount") ?? 0;           // OUTPUT parameter is read after the reader is consumed
        int pageSize = Math.Clamp(q.PageSize, 1, 100);
        return new PagedResult<ItemListItemDto>(items, total, Math.Max(1, q.Page), pageSize);
    }

    public async Task<IReadOnlyList<ItemStockDto>> GetStockAsync(int itemId, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ItemStockDto>(new CommandDefinition(
            "SELECT WarehouseId, WarehouseCode, WarehouseName, Quantity FROM dbo.fn_ItemStock(@itemId) ORDER BY WarehouseCode;",
            new { itemId }, cancellationToken: ct));                  // inline table-valued function
        return rows.AsList();
    }

    /// <summary>TEMPORAL TABLE query through EF Core: every version of the row from Items + ItemsHistory.</summary>
    public async Task<IReadOnlyList<ItemHistoryDto>> GetHistoryAsync(int itemId, CancellationToken ct = default) =>
        await db.Items.TemporalAll()
            .IgnoreQueryFilters()                                     // show the version where it was soft-deleted too
            .Where(i => i.Id == itemId)
            .OrderBy(i => EF.Property<DateTime>(i, "ValidFrom"))
            .Select(i => new ItemHistoryDto(i.Name, i.UnitPrice, i.TaxPercent, i.IsActive, i.IsDeleted,
                EF.Property<DateTime>(i, "ValidFrom"), EF.Property<DateTime>(i, "ValidTo")))
            .ToListAsync(ct);

    /// <summary>IAsyncEnumerable: EF streams rows from the open DataReader one by one.</summary>
    public async IAsyncEnumerable<ItemDto> StreamAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = db.Items.AsNoTracking()
            .OrderBy(i => i.Code)
            .Select(i => new ItemDto(i.Id, i.Code, i.Name, i.CategoryId, i.Category.Name, i.Unit, i.UnitPrice,
                i.TaxPercent, i.ReorderLevel, i.IsActive, "", i.CreatedBy, i.CreatedOn, i.ModifiedBy, i.ModifiedOn))
            .AsAsyncEnumerable();

        await foreach (var item in query.WithCancellation(ct))
            yield return item;
    }

    private sealed class ItemRow
    {
        public int Id { get; init; }
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public int CategoryId { get; init; }
        public string CategoryName { get; init; } = "";
        public string Unit { get; init; } = "";
        public decimal UnitPrice { get; init; }
        public decimal TaxPercent { get; init; }
        public decimal ReorderLevel { get; init; }
        public bool IsActive { get; init; }
        public decimal StockQty { get; init; }
        public bool IsBelowReorder { get; init; }
        public byte[] RowVersion { get; init; } = [];
    }
}

internal sealed class OrderQueries(SqlConnectionFactory factory) : IOrderQueries
{
    public async Task<PagedResult<OrderSummaryDto>> GetPagedAsync(OrderQuery q, CancellationToken ct = default)
    {
        int page = Math.Max(1, q.Page), pageSize = Math.Clamp(q.PageSize, 1, 100);

        // Optional filters in one parameterised "catch-all" WHERE; RECOMPILE avoids a
        // plan sniffed for one filter combination being reused for another.
        const string where = """
            WHERE (@Status IS NULL OR Status = @Status)
              AND (@CustomerId IS NULL OR CustomerId = @CustomerId)
              AND (@FromDate IS NULL OR OrderDate >= @FromDate)
              AND (@ToDate IS NULL OR OrderDate <= @ToDate)
              AND (@Search IS NULL OR OrderNo LIKE @Search OR CustomerName LIKE @Search)
            """;
        string sql = $"""
            SELECT COUNT(*) FROM dbo.vw_OrderSummary {where} OPTION (RECOMPILE);

            SELECT Id, OrderNo, OrderDate, Status, CustomerCode, CustomerName, Warehouse, TotalAmount, LineCount, CreatedBy
            FROM dbo.vw_OrderSummary {where}
            ORDER BY OrderDate DESC, Id DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            OPTION (RECOMPILE);
            """;   // 'where' is a constant in code, never user input: safe to interpolate

        var p = new DynamicParameters();
        p.Add("@Status", string.IsNullOrWhiteSpace(q.Status) ? null : q.Status, DbType.AnsiString, size: 20);
        p.Add("@CustomerId", q.CustomerId);
        p.Add("@FromDate", q.FromDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        p.Add("@ToDate", q.ToDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        p.Add("@Search", string.IsNullOrWhiteSpace(q.Search) ? null : $"%{q.Search.Trim()}%", DbType.String, size: 160);
        p.Add("@Skip", (page - 1) * pageSize);
        p.Add("@Take", pageSize);

        await using var conn = await factory.OpenAsync(ct);
        // QueryMultiple: two result sets in ONE round trip
        await using var multi = await conn.QueryMultipleAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        int total = await multi.ReadSingleAsync<int>();
        var rows = await multi.ReadAsync<OrderRow>();

        var items = rows.Select(r => new OrderSummaryDto(r.Id, r.OrderNo, DateOnly.FromDateTime(r.OrderDate), r.Status,
            r.CustomerCode, r.CustomerName, r.Warehouse, r.TotalAmount, r.LineCount, r.CreatedBy)).ToList();
        return new PagedResult<OrderSummaryDto>(items, total, page, pageSize);
    }

    private sealed class OrderRow
    {
        public int Id { get; init; }
        public string OrderNo { get; init; } = "";
        public DateTime OrderDate { get; init; }
        public string Status { get; init; } = "";
        public string CustomerCode { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string Warehouse { get; init; } = "";
        public decimal TotalAmount { get; init; }
        public int LineCount { get; init; }
        public string CreatedBy { get; init; } = "";
    }
}

internal sealed class StockQueries(SqlConnectionFactory factory) : IStockQueries
{
    public async Task<IReadOnlyList<StockSummaryDto>> GetSummaryAsync(int? warehouseId, string? status, CancellationToken ct = default)
    {
        const string sql = """
            SELECT ItemId, Code, Name, Category, WarehouseId, Warehouse, Quantity, Unit, ReorderLevel, StockStatus, StockValue
            FROM dbo.vw_StockSummary
            WHERE (@WarehouseId IS NULL OR WarehouseId = @WarehouseId)
              AND (@Status IS NULL OR StockStatus = @Status)
            ORDER BY Code, Warehouse;
            """;
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<StockSummaryDto>(new CommandDefinition(sql,
            new { WarehouseId = warehouseId, Status = new DbString { Value = status, IsAnsi = true, Length = 10 } },
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<RunningStockDto>> GetRunningStockAsync(int itemId, int? warehouseId, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<RunningStockDto>(new CommandDefinition("dbo.usp_GetRunningStock",
            new { ItemId = itemId, WarehouseId = warehouseId }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.AsList();
    }
}

internal sealed class ReportQueries(SqlConnectionFactory factory) : IReportQueries
{
    public async Task<IReadOnlyList<MonthlySalesDto>> GetMonthlySalesAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<MonthlySalesDto>(new CommandDefinition(
            "dbo.usp_GetMonthlySales", commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<LatestOrderDto>> GetLatestOrdersAsync(int topN, bool includeCustomersWithoutOrders, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<LatestOrderRow>(new CommandDefinition("dbo.usp_GetLatestOrdersPerCustomer",
            new { TopN = topN, IncludeNoOrders = includeCustomersWithoutOrders },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.Select(r => new LatestOrderDto(r.Code, r.Name, r.OrderNo,
            r.OrderDate is { } d ? DateOnly.FromDateTime(d) : null, r.Status, r.TotalAmount)).ToList();
    }

    public async Task<IReadOnlyList<StatusCountDto>> GetOrderStatusCountsAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<StatusCountDto>(new CommandDefinition(
            "SELECT Status, COUNT(*) AS Count FROM dbo.Orders WHERE IsDeleted = 0 GROUP BY Status ORDER BY Count DESC;",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<int> GetLowStockCountAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.vw_StockSummary WHERE StockStatus = 'Reorder';", cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TopItemDto>> GetTopItemsAsync(int topN, CancellationToken ct = default)
    {
        // Reads the INDEXED VIEW's stored results directly; NOEXPAND tells the
        // optimiser to use the view's index instead of re-aggregating OrderLines.
        const string sql = """
            SELECT TOP (@topN) i.Code, i.Name, v.TotalQty AS Qty, v.GrossAmount AS Amount
            FROM dbo.vw_ItemSalesTotals v WITH (NOEXPAND)
            JOIN dbo.Items i ON i.Id = v.ItemId
            ORDER BY v.GrossAmount DESC;
            """;
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<TopItemDto>(new CommandDefinition(sql, new { topN }, cancellationToken: ct));
        return rows.AsList();
    }

    private sealed class LatestOrderRow
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public string? OrderNo { get; init; }
        public DateTime? OrderDate { get; init; }
        public string? Status { get; init; }
        public decimal? TotalAmount { get; init; }
    }
}
