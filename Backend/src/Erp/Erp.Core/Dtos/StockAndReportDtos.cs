using System.ComponentModel.DataAnnotations;

namespace Erp.Core.Dtos;

public sealed record StockSummaryDto(
    int ItemId, string Code, string Name, string Category, int WarehouseId, string Warehouse,
    decimal Quantity, string Unit, decimal ReorderLevel, string StockStatus, decimal StockValue);

public sealed record RunningStockDto(
    long Id, string Warehouse, DateTime TxnDate, string TxnType, string? RefDocNo, decimal Qty, decimal RunningStock);

/// <summary>Qty is signed: +10 adds stock, -3 removes it.</summary>
public sealed record StockAdjustmentRequest(
    [Range(1, int.MaxValue)] int ItemId,
    [Range(1, int.MaxValue)] int WarehouseId,
    decimal Qty,
    [Required, StringLength(200, MinimumLength = 3)] string Reason);

public sealed record StockAdjustmentResultDto(int ItemId, int WarehouseId, decimal OldQuantity, decimal NewQuantity, long LedgerEntryId);

public sealed record MonthlySalesDto(
    string Month, string FinancialYear, int Orders, decimal Total, decimal? PrevMonthTotal,
    decimal? Change, decimal? ChangePct, decimal FyToDate);

public sealed record LatestOrderDto(string Code, string Name, string? OrderNo, DateOnly? OrderDate, string? Status, decimal? TotalAmount);

public sealed record StatusCountDto(string Status, int Count);

public sealed record TopItemDto(string Code, string Name, decimal Qty, decimal Amount);

public sealed record DashboardDto(
    IReadOnlyList<StatusCountDto> OrdersByStatus,
    int LowStockCount,
    IReadOnlyList<MonthlySalesDto> RecentMonths,
    IReadOnlyList<TopItemDto> TopItems,
    long ElapsedMs);
