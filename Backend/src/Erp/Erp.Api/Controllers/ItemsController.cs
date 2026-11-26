using Asp.Versioning;
using Erp.Api.Authorization;
using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Services;
using Microsoft.AspNetCore.Mvc;
using MiniErp.Contracts;

namespace Erp.Api.Controllers;

/// <summary>
/// Item master REST resource: /api/v1/items
///   GET    /items              list (paged, filtered, sorted)        200
///   GET    /items/{id}         one item, with an ETag                200 / 304 / 404
///   POST   /items              create                                201 + Location
///   PUT    /items/{id}         replace (needs If-Match / rowVersion)  200 / 409
///   DELETE /items/{id}         soft delete                           204
/// [ApiController]: automatic 400 on invalid models, binding-source inference
/// (complex types from the body, simple ones from route/query).
/// ControllerBase (not Controller): an API has no views.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/items")]
[Produces("application/json")]
public sealed class ItemsController(ItemService items) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.ItemsView)]
    [ProducesResponseType<ApiResponse<PagedResult<ItemListItemDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<ItemListItemDto>>>> GetItems(
        [FromQuery] ItemQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<ItemListItemDto>>.Ok(await items.GetPagedAsync(query, ct)));

    /// <summary>
    /// ActionResult&lt;T&gt; lets the method return either the typed body or a status
    /// result (NotFound, 304), and Swagger still knows the response type.
    /// ETag = the row version: a client that sends If-None-Match with the same
    /// value gets 304 Not Modified and no body (HTTP caching).
    /// </summary>
    [HttpGet("{id:int}", Name = "GetItemById")]     // ':int' route constraint: /items/abc doesn't match -> 404
    [HasPermission(PermissionCodes.ItemsView)]
    [ProducesResponseType<ApiResponse<ItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ItemDto>>> GetItem([FromRoute] int id,
        [FromHeader(Name = "If-None-Match")] string? ifNoneMatch, CancellationToken ct)
    {
        var item = await items.GetByIdAsync(id, ct);
        string etag = $"\"{item.RowVersion}\"";
        if (ifNoneMatch == etag) return StatusCode(StatusCodes.Status304NotModified);

        Response.Headers.ETag = etag;
        return Ok(ApiResponse<ItemDto>.Ok(item));
    }

    /// <summary>Used by the Angular async validator while the user types a code.</summary>
    [HttpGet("check-code")]
    [HasPermission(PermissionCodes.ItemsView)]
    public async Task<ActionResult<ApiResponse<bool>>> CheckCode([FromQuery] string code, [FromQuery] int? excludeId, CancellationToken ct) =>
        Ok(ApiResponse<bool>.Ok(await items.IsCodeAvailableAsync(code, excludeId, ct)));

    [HttpGet("{id:int}/stock")]
    [HasPermission(PermissionCodes.StockView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ItemStockDto>>>> GetStock([FromRoute] int id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ItemStockDto>>.Ok(await items.GetStockAsync(id, ct)));

    /// <summary>Every past version of the item (temporal table): price changes, renames, deletes.</summary>
    [HttpGet("{id:int}/history")]
    [HasPermission(PermissionCodes.ItemsView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ItemHistoryDto>>>> GetHistory([FromRoute] int id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ItemHistoryDto>>.Ok(await items.GetHistoryAsync(id, ct)));

    /// <summary>CSV streamed row by row (IAsyncEnumerable + yield): memory stays flat for any size.</summary>
    [HttpGet("export")]
    [HasPermission(PermissionCodes.ItemsView)]
    [Produces("text/csv")]
    public async Task Export(CancellationToken ct)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=items-{DateTime.UtcNow:yyyyMMdd}.csv";
        await foreach (string line in items.ExportCsvAsync(ct))
            await Response.WriteAsync(line + "\r\n", ct);
    }

    /// <summary>201 Created + Location header pointing at GET /items/{id}.</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.ItemsCreate)]
    [ProducesResponseType<ApiResponse<ItemDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ItemDto>>> Create([FromBody] CreateItemRequest request, CancellationToken ct)
    {
        var item = await items.CreateAsync(request, ct);
        Response.Headers.ETag = $"\"{item.RowVersion}\"";
        return CreatedAtRoute("GetItemById",
            new { id = item.Id, version = RouteData.Values["version"]?.ToString() ?? "1" },
            ApiResponse<ItemDto>.Ok(item, $"Item {item.Code} created."));
    }

    /// <summary>
    /// PUT = replace the whole resource (idempotent: sending it twice gives the
    /// same state). Optimistic concurrency: send the row version from GET either
    /// as If-Match header or in the body; if someone saved in between -> 409.
    /// </summary>
    [HttpPut("{id:int}")]
    [HasPermission(PermissionCodes.ItemsEdit)]
    [ProducesResponseType<ApiResponse<ItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ItemDto>>> Update([FromRoute] int id, [FromBody] UpdateItemRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(ifMatch))
            request = request with { RowVersion = ifMatch.Trim('"') };   // record 'with': copy with one change

        var item = await items.UpdateAsync(id, request, ct);
        Response.Headers.ETag = $"\"{item.RowVersion}\"";
        return Ok(ApiResponse<ItemDto>.Ok(item, $"Item {item.Code} updated."));
    }

    [HttpDelete("{id:int}")]
    [HasPermission(PermissionCodes.ItemsDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken ct)
    {
        await items.DeleteAsync(id, ct);
        return NoContent();                                // 204: success, no body
    }
}
