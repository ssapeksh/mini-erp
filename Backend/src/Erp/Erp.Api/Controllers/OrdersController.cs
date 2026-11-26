using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Erp.Api.Authorization;
using Erp.Api.Filters;
using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Services;
using Microsoft.AspNetCore.Mvc;
using MiniErp.Contracts;

namespace Erp.Api.Controllers;

/// <summary>
/// Sales orders with a two-level approval workflow:
/// Draft -> submit -> PendingL1 -> approve(L1) -> PendingL2 -> approve(L2) -> Approved -> despatch -> Despatched
/// Actions that aren't plain CRUD are modelled as sub-resources: POST /orders/{id}/approve.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Produces("application/json")]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.OrdersView)]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderSummaryDto>>>> GetOrders([FromQuery] OrderQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<OrderSummaryDto>>.Ok(await orders.GetPagedAsync(query, ct)));

    [HttpGet("{id:int}", Name = "GetOrderById")]
    [HasPermission(PermissionCodes.OrdersView)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrder([FromRoute] int id, CancellationToken ct) =>
        Ok(ApiResponse<OrderDto>.Ok(await orders.GetByIdAsync(id, ct)));

    /// <summary>Look up by document number, e.g. ?orderNo=SO/2026-27/000042 (parsed with Span&lt;char&gt;).</summary>
    [HttpGet("by-number")]
    [HasPermission(PermissionCodes.OrdersView)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetByNumber([FromQuery, Required] string orderNo, CancellationToken ct) =>
        Ok(ApiResponse<OrderDto>.Ok(await orders.GetByNumberAsync(orderNo, ct)));

    /// <summary>
    /// POST is NOT idempotent by nature: a retried request would create a second
    /// order. [Idempotent] + the Idempotency-Key header fix that: a retry with
    /// the same key gets the first response back (header Idempotent-Replayed: true).
    /// </summary>
    [HttpPost]
    [Idempotent]
    [HasPermission(PermissionCodes.OrdersCreate)]
    [ProducesResponseType<ApiResponse<OrderDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Create(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = IdempotentAttribute.HeaderName), Required] string idempotencyKey,
        CancellationToken ct)
    {
        var order = await orders.CreateAsync(request, idempotencyKey, ct);
        return CreatedAtRoute("GetOrderById",
            new { id = order.Id, version = RouteData.Values["version"]?.ToString() ?? "1" },
            ApiResponse<OrderDto>.Ok(order, $"Order {order.OrderNo} created as Draft."));
    }

    [HttpPost("{id:int}/submit")]
    [HasPermission(PermissionCodes.OrdersCreate)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Submit([FromRoute] int id, CancellationToken ct) =>
        Ok(ApiResponse<OrderDto>.Ok(await orders.SubmitAsync(id, ct), "Submitted for level-1 approval."));

    /// <summary>The endpoint admits either approver; the service checks the level-specific permission (403 otherwise).</summary>
    [HttpPost("{id:int}/approve")]
    [HasPermission(PermissionCodes.OrdersApproveL1, PermissionCodes.OrdersApproveL2)]
    [ProducesResponseType<ApiResponse<OrderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Approve([FromRoute] int id, [FromBody] ApproveOrderRequest request, CancellationToken ct)
    {
        var order = await orders.ApproveAsync(id, request.Comments, ct);
        return Ok(ApiResponse<OrderDto>.Ok(order, order.Status == "Approved" ? "Order approved." : "Level-1 approved; waiting for level 2."));
    }

    [HttpPost("{id:int}/reject")]
    [HasPermission(PermissionCodes.OrdersApproveL1, PermissionCodes.OrdersApproveL2)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Reject([FromRoute] int id, [FromBody] RejectOrderRequest request, CancellationToken ct) =>
        Ok(ApiResponse<OrderDto>.Ok(await orders.RejectAsync(id, request.Comments, ct), "Order rejected."));

    /// <summary>Runs usp_DespatchOrder: 409 if not Approved, 422 if stock is short.</summary>
    [HttpPost("{id:int}/despatch")]
    [HasPermission(PermissionCodes.OrdersDespatch)]
    [ProducesResponseType<ApiResponse<DespatchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<DespatchResultDto>>> Despatch([FromRoute] int id, CancellationToken ct)
    {
        var result = await orders.DespatchAsync(id, ct);
        return Ok(ApiResponse<DespatchResultDto>.Ok(result, $"Order {result.OrderNo} despatched."));
    }

    [HttpDelete("{id:int}")]
    [HasPermission(PermissionCodes.OrdersCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken ct)
    {
        await orders.DeleteAsync(id, ct);
        return NoContent();
    }
}
