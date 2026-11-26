using System.ComponentModel.DataAnnotations;

namespace Erp.Core.Dtos;

public sealed class OrderQuery
{
    public string? Status { get; set; }
    public int? CustomerId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string? Search { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed record OrderSummaryDto(
    int Id, string OrderNo, DateOnly OrderDate, string Status, string CustomerCode, string CustomerName,
    string Warehouse, decimal TotalAmount, int LineCount, string CreatedBy);

public sealed record CreateOrderLineRequest(
    [Range(1, int.MaxValue)] int ItemId,
    [Range(0.001, 1_000_000)] decimal Qty);

public sealed record CreateOrderRequest(
    [Range(1, int.MaxValue)] int CustomerId,
    [Range(1, int.MaxValue)] int WarehouseId,
    DateOnly? OrderDate,
    [StringLength(500)] string? Remarks,
    [Required] IReadOnlyList<CreateOrderLineRequest> Lines);

public sealed record OrderLineDto(
    short LineNumber, int ItemId, string ItemCode, string ItemName, decimal Qty, decimal UnitPrice,
    decimal DiscountPct, decimal TaxPercent, decimal LineTotal);

public sealed record ApprovalDto(byte Level, string Action, string ActionBy, DateTime ActionOn, string? Comments);

public sealed record OrderDto(
    int Id, string OrderNo, int CustomerId, string CustomerName, int WarehouseId, string WarehouseCode,
    DateOnly OrderDate, string Status, byte? PendingLevel, decimal SubTotal, decimal TaxAmount, decimal TotalAmount,
    string? Remarks, string RowVersion, string CreatedBy, DateTime CreatedOn,
    IReadOnlyList<OrderLineDto> Lines, IReadOnlyList<ApprovalDto> Approvals);

public sealed record ApproveOrderRequest([StringLength(500)] string? Comments);

public sealed record RejectOrderRequest([Required, StringLength(500, MinimumLength = 3)] string Comments);

public sealed record DespatchResultDto(int OrderId, string OrderNo, string Status);
