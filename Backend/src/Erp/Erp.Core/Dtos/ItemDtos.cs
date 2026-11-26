using System.ComponentModel.DataAnnotations;

namespace Erp.Core.Dtos;

/*  DTOs (Data Transfer Objects) shape what the API sends and receives:
    - hide internal columns (PasswordHash, IsDeleted...)
    - stop OVER-POSTING: a client can't set CreatedBy or RowVersion fields
      that aren't on the request type
    - no navigation properties, so no circular references in JSON
    Positional records = concise, immutable, value equality.                 */

/// <summary>Query-string parameters for GET /items. A class (not a record) so [FromQuery] can bind each property.</summary>
public sealed class ItemQuery
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public string SortBy { get; set; } = "Name";      // Name | Code | Price | Stock
    public string SortDir { get; set; } = "ASC";
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed record ItemListItemDto(
    int Id, string Code, string Name, int CategoryId, string CategoryName, string Unit,
    decimal UnitPrice, decimal TaxPercent, decimal ReorderLevel, bool IsActive,
    decimal StockQty, bool IsBelowReorder, string RowVersion);

public sealed record ItemDto(
    int Id, string Code, string Name, int CategoryId, string CategoryName, string Unit,
    decimal UnitPrice, decimal TaxPercent, decimal ReorderLevel, bool IsActive, string RowVersion,
    string CreatedBy, DateTime CreatedOn, string? ModifiedBy, DateTime? ModifiedOn);

/// <summary>Data annotations do the simple checks ([ApiController] returns 400 automatically);
/// FluentValidation (CreateItemRequestValidator) does the business-format rules.</summary>
public sealed record CreateItemRequest(
    [Required, StringLength(30)] string Code,
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [Range(1, int.MaxValue)] int CategoryId,
    [Required, StringLength(10)] string Unit,
    [Range(0, 10_000_000)] decimal UnitPrice,
    decimal TaxPercent,
    [Range(0, 1_000_000)] decimal ReorderLevel);

/// <summary>RowVersion (base64) is what the client read; if the row changed since, the API returns 409.</summary>
public sealed record UpdateItemRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [Range(1, int.MaxValue)] int CategoryId,
    [Required, StringLength(10)] string Unit,
    [Range(0, 10_000_000)] decimal UnitPrice,
    decimal TaxPercent,
    [Range(0, 1_000_000)] decimal ReorderLevel,
    bool IsActive,
    [Required] string RowVersion);

public sealed record ItemStockDto(int WarehouseId, string WarehouseCode, string WarehouseName, decimal Quantity);

public sealed record ItemHistoryDto(string Name, decimal UnitPrice, decimal TaxPercent, bool IsActive, bool IsDeleted,
                                    DateTime ValidFrom, DateTime ValidTo);

public sealed record LookupDto(int Id, string Code, string Name);

public sealed record CustomerDto(int Id, string Code, string Name, string? City, string? Email, decimal CreditLimit);
