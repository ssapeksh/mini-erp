using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Entities;
using Erp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using MiniErp.Contracts;

namespace Erp.Core.Services;

/// <summary>
/// Item master use cases. ABSTRACTION: controllers call these methods and
/// never see SQL, EF or Dapper. DEPENDENCY INVERSION: every dependency is an
/// interface injected through the (primary) constructor.
/// </summary>
public sealed class ItemService(
    IRepository<Item> items,
    IRepository<ItemCategory> categories,
    IUnitOfWork uow,
    IItemQueries queries)
{
    /// <summary>Read side: Dapper + stored procedure usp_GetItemsPaged.</summary>
    public Task<PagedResult<ItemListItemDto>> GetPagedAsync(ItemQuery query, CancellationToken ct = default)
        => queries.GetPagedAsync(query, ct);

    public async Task<ItemDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        // AsNoTracking + projection: SQL selects only the DTO's columns, no change tracking.
        // Include isn't needed: i.Category.Name in a projection becomes a JOIN.
        var dto = await items.QueryNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new ItemDto(
                i.Id, i.Code, i.Name, i.CategoryId, i.Category.Name, i.Unit, i.UnitPrice, i.TaxPercent,
                i.ReorderLevel, i.IsActive, Convert.ToBase64String(i.RowVersion),   // client-evaluated in the final projection
                i.CreatedBy, i.CreatedOn, i.ModifiedBy, i.ModifiedOn))
            .FirstOrDefaultAsync(ct);

        return dto ?? throw NotFoundException.For("Item", id);   // ?? + throw expression
    }

    public async Task<ItemDto> CreateAsync(CreateItemRequest request, CancellationToken ct = default)
    {
        string code = request.Code.Trim().ToUpperInvariant();
        if (!await IsCodeAvailableAsync(code, null, ct))
            throw new ConflictException($"Item code '{code}' already exists.");
        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        var item = new Item
        {
            Code = code,
            Name = request.Name.Trim(),
            CategoryId = request.CategoryId,
            Unit = request.Unit.Trim().ToUpperInvariant(),
            UnitPrice = request.UnitPrice,
            TaxPercent = request.TaxPercent,
            ReorderLevel = request.ReorderLevel
        };
        items.Add(item);
        await uow.SaveChangesAsync(ct);          // interceptor fills CreatedBy / CreatedOn
        return await GetByIdAsync(item.Id, ct);
    }

    public async Task<ItemDto> UpdateAsync(int id, UpdateItemRequest request, CancellationToken ct = default)
    {
        var item = await items.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Item", id);
        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        // OPTIMISTIC CONCURRENCY: EF adds "WHERE RowVersion = <what the client saw>".
        // If another user saved in between, 0 rows update -> ConcurrencyException -> HTTP 409.
        uow.SetOriginalRowVersion(item, Convert.FromBase64String(request.RowVersion));

        item.Name = request.Name.Trim();
        item.CategoryId = request.CategoryId;
        item.Unit = request.Unit.Trim().ToUpperInvariant();
        item.UnitPrice = request.UnitPrice;
        item.TaxPercent = request.TaxPercent;
        item.ReorderLevel = request.ReorderLevel;
        item.IsActive = request.IsActive;

        await uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    /// <summary>Soft delete. Business rule: an item that still has stock can't be deleted.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var item = await items.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Item", id);

        decimal onHand = await uow.Set<StockBalance>()
            .Where(b => b.ItemId == id)
            .SumAsync(b => b.Quantity, ct);
        if (onHand > 0)
            throw new BusinessRuleException($"Item {item.Code} still has {onHand:0.###} in stock; adjust stock to zero before deleting.");

        items.Remove(item);                      // interceptor converts DELETE into IsDeleted = 1
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Used by the Angular async validator. Checks deleted items too (the DB unique key does).</summary>
    public async Task<bool> IsCodeAvailableAsync(string code, int? excludeId, CancellationToken ct = default)
    {
        string normalised = code.Trim().ToUpperInvariant();
        return !await items.QueryNoTracking()
            .IgnoreQueryFilters()                // include soft-deleted rows
            .AnyAsync(i => i.Code == normalised && (excludeId == null || i.Id != excludeId), ct);
    }

    public Task<IReadOnlyList<ItemStockDto>> GetStockAsync(int id, CancellationToken ct = default)
        => queries.GetStockAsync(id, ct);

    public Task<IReadOnlyList<ItemHistoryDto>> GetHistoryAsync(int id, CancellationToken ct = default)
        => queries.GetHistoryAsync(id, ct);

    /// <summary>Streams every item as CSV lines without loading them all into memory.</summary>
    public IAsyncEnumerable<string> ExportCsvAsync(CancellationToken ct = default) =>
        CsvExporter.ToCsvLinesAsync(queries.StreamAllAsync(ct),
        [
            ("Code", i => i.Code), ("Name", i => i.Name), ("Category", i => i.CategoryName),
            ("Unit", i => i.Unit), ("UnitPrice", i => i.UnitPrice), ("TaxPercent", i => i.TaxPercent),
            ("ReorderLevel", i => i.ReorderLevel), ("Active", i => i.IsActive)
        ], ct);

    private async Task EnsureCategoryExistsAsync(int categoryId, CancellationToken ct)
    {
        if (!await categories.QueryNoTracking().AnyAsync(c => c.Id == categoryId, ct))
            throw new BusinessRuleException($"Category {categoryId} does not exist.");
    }
}
