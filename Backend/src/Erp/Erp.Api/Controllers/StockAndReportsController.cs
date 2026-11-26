using System.Diagnostics;
using Asp.Versioning;
using Erp.Api.Authorization;
using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Interfaces;
using Erp.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using MiniErp.Contracts;

namespace Erp.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/stock")]
[Produces("application/json")]
public sealed class StockController(StockService stock) : ControllerBase
{
    /// <summary>Stock per item and warehouse; filter by ?warehouseId=1&amp;status=Reorder|Low|OK.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.StockView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StockSummaryDto>>>> GetSummary(
        [FromQuery] int? warehouseId, [FromQuery] string? status, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<StockSummaryDto>>.Ok(await stock.GetSummaryAsync(warehouseId, status, ct)));

    /// <summary>Stock ledger with a running balance (window function SUM() OVER in usp_GetRunningStock).</summary>
    [HttpGet("items/{itemId:int}/ledger")]
    [HasPermission(PermissionCodes.StockView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RunningStockDto>>>> GetLedger(
        [FromRoute] int itemId, [FromQuery] int? warehouseId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<RunningStockDto>>.Ok(await stock.GetRunningStockAsync(itemId, warehouseId, ct)));

    [HttpPost("adjustments")]
    [HasPermission(PermissionCodes.StockAdjust)]
    [ProducesResponseType<ApiResponse<StockAdjustmentResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<StockAdjustmentResultDto>>> Adjust([FromBody] StockAdjustmentRequest request, CancellationToken ct)
    {
        var result = await stock.AdjustAsync(request, ct);
        return Ok(ApiResponse<StockAdjustmentResultDto>.Ok(result, $"Stock changed from {result.OldQuantity:0.###} to {result.NewQuantity:0.###}."));
    }
}

/// <summary>
/// API VERSIONING: the same URL in two versions. v2 of monthly-sales returns a
/// different shape (a breaking change), so it gets a new version instead of
/// changing v1 under existing clients. Requested as /api/v2/... (URL segment)
/// or with the X-Api-Version header; responses list supported versions in
/// api-supported-versions.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/reports")]
[Produces("application/json")]
[HasPermission(PermissionCodes.ReportsView)]
public sealed class ReportsController(IReportQueries reports) : ControllerBase
{
    [HttpGet("monthly-sales")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MonthlySalesDto>>>> MonthlySalesV1(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<MonthlySalesDto>>.Ok(await reports.GetMonthlySalesAsync(ct)));

    [HttpGet("monthly-sales")]
    [MapToApiVersion("2.0")]
    public async Task<ActionResult<ApiResponse<MonthlySalesV2Dto>>> MonthlySalesV2(CancellationToken ct)
    {
        var months = await reports.GetMonthlySalesAsync(ct);
        var best = months.MaxBy(m => m.Total);
        var summary = new MonthlySalesSummary(
            TotalSales: months.Sum(m => m.Total),
            AverageMonthly: months.Count == 0 ? 0 : Math.Round(months.Average(m => m.Total), 2),
            BestMonth: best?.Month,
            BestMonthTotal: best?.Total);
        return Ok(ApiResponse<MonthlySalesV2Dto>.Ok(new MonthlySalesV2Dto(summary, months)));
    }

    /// <summary>
    /// OUTPUT CACHE (server side): the response is stored on the server for
    /// 60 seconds, varying by query string; the database is hit at most once a minute.
    /// </summary>
    [HttpGet("latest-orders")]
    [OutputCache(Duration = 60, VaryByQueryKeys = ["topN", "includeCustomersWithoutOrders"])]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LatestOrderDto>>>> LatestOrders(
        [FromQuery] int topN = 3, [FromQuery] bool includeCustomersWithoutOrders = true, CancellationToken ct = default) =>
        Ok(ApiResponse<IReadOnlyList<LatestOrderDto>>.Ok(
            await reports.GetLatestOrdersAsync(Math.Clamp(topN, 1, 20), includeCustomersWithoutOrders, ct)));

    /// <summary>
    /// Four independent queries run IN PARALLEL with Task.WhenAll (coding
    /// problem 19): total time is the slowest query, not the sum of all four.
    /// Safe here because each Dapper query opens its OWN connection; the same
    /// DbContext must never be used by two tasks at once (not thread-safe).
    /// [ResponseCache] adds Cache-Control so the BROWSER may reuse it for 30 s.
    /// </summary>
    [HttpGet("dashboard")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> Dashboard(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        Task<IReadOnlyList<StatusCountDto>> statusTask = reports.GetOrderStatusCountsAsync(ct);
        Task<int> lowStockTask = reports.GetLowStockCountAsync(ct);
        Task<IReadOnlyList<MonthlySalesDto>> salesTask = reports.GetMonthlySalesAsync(ct);
        Task<IReadOnlyList<TopItemDto>> topItemsTask = reports.GetTopItemsAsync(5, ct);

        await Task.WhenAll(statusTask, lowStockTask, salesTask, topItemsTask);

        // .Result is safe ONLY because WhenAll guarantees they're complete. Never
        // call .Result/.Wait() on an unfinished task: it blocks a thread and can deadlock.
        var dto = new DashboardDto(statusTask.Result, lowStockTask.Result,
            salesTask.Result.TakeLast(6).ToList(), topItemsTask.Result, sw.ElapsedMilliseconds);
        return Ok(ApiResponse<DashboardDto>.Ok(dto));
    }
}

public sealed record MonthlySalesSummary(decimal TotalSales, decimal AverageMonthly, string? BestMonth, decimal? BestMonthTotal);
public sealed record MonthlySalesV2Dto(MonthlySalesSummary Summary, IReadOnlyList<MonthlySalesDto> Months);
