using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Entities;
using Erp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Core.Services;

public sealed class StockService(
    IRepository<Item> items,
    IRepository<Warehouse> warehouses,
    IUnitOfWork uow,
    IStockQueries queries,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public Task<IReadOnlyList<StockSummaryDto>> GetSummaryAsync(int? warehouseId, string? status, CancellationToken ct = default)
        => queries.GetSummaryAsync(warehouseId, status, ct);

    public Task<IReadOnlyList<RunningStockDto>> GetRunningStockAsync(int itemId, int? warehouseId, CancellationToken ct = default)
        => queries.GetRunningStockAsync(itemId, warehouseId, ct);

    /// <summary>
    /// Manual stock adjustment with an EXPLICIT EF TRANSACTION.
    /// One SaveChanges is already atomic; the explicit transaction is needed
    /// here because there are TWO saves: the ledger row's identity Id is only
    /// known after the first save, and the reference number (ADJ/000123) is
    /// built from it in the second. Both commit together or neither does.
    /// </summary>
    public async Task<StockAdjustmentResultDto> AdjustAsync(StockAdjustmentRequest request, CancellationToken ct = default)
    {
        var item = await items.QueryNoTracking().FirstOrDefaultAsync(i => i.Id == request.ItemId, ct)
                   ?? throw NotFoundException.For("Item", request.ItemId);
        if (!await warehouses.QueryNoTracking().AnyAsync(w => w.Id == request.WarehouseId, ct))
            throw NotFoundException.For("Warehouse", request.WarehouseId);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        await using IUnitOfWorkTransaction tx = await uow.BeginTransactionAsync(ct);

        var balance = await uow.Set<StockBalance>()
            .FirstOrDefaultAsync(b => b.ItemId == request.ItemId && b.WarehouseId == request.WarehouseId, ct);
        if (balance is null)
        {
            balance = new StockBalance { ItemId = request.ItemId, WarehouseId = request.WarehouseId, Quantity = 0 };
            uow.Set<StockBalance>().Add(balance);
        }

        decimal oldQty = balance.Quantity;
        decimal newQty = oldQty + request.Qty;
        if (newQty < 0)
            throw new BusinessRuleException($"Cannot remove {-request.Qty:0.###} {item.Unit} of {item.Code}: only {oldQty:0.###} in stock.");

        balance.Quantity = newQty;          // RowVersion on StockBalances: a parallel adjustment gets 409
        balance.ModifiedOn = now;

        var entry = new StockLedgerEntry
        {
            ItemId = request.ItemId,
            WarehouseId = request.WarehouseId,
            TxnDate = now,
            TxnType = StockTxnTypes.Adjustment,
            Qty = request.Qty,
            Remarks = request.Reason.Trim(),
            CreatedBy = currentUser.UserName
        };
        uow.Set<StockLedgerEntry>().Add(entry);

        await uow.SaveChangesAsync(ct);              // save 1: entry.Id is generated now
        entry.RefDocNo = $"ADJ/{entry.Id:000000}";
        await uow.SaveChangesAsync(ct);              // save 2: same transaction
        await tx.CommitAsync(ct);                    // disposing without Commit = rollback

        return new StockAdjustmentResultDto(request.ItemId, request.WarehouseId, oldQty, newQty, entry.Id);
    }
}
