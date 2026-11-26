using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Entities;
using Erp.Core.Interfaces;
using Erp.Core.Pricing;
using Microsoft.EntityFrameworkCore;
using MiniErp.Contracts;

namespace Erp.Core.Services;

/// <summary>Sales order use cases: create, submit, two-level approval, despatch.</summary>
public sealed class OrderService(
    IRepository<Order> orders,
    IRepository<Customer> customers,
    IRepository<Warehouse> warehouses,
    IRepository<Item> items,
    IUnitOfWork uow,
    IOrderQueries queries,
    IOrderProcedures procedures,
    IPricingStrategyFactory pricing,
    ITaxRateProvider taxRates,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public Task<PagedResult<OrderSummaryDto>> GetPagedAsync(OrderQuery query, CancellationToken ct = default)
        => queries.GetPagedAsync(query, ct);

    public async Task<OrderDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        // EAGER LOADING with Include / ThenInclude. Without it, touching
        // order.Lines[i].Item in a loop would fire one query per line (N+1).
        // AsSplitQuery: 3 simple queries instead of one JOIN whose rows multiply
        // (lines x approvals = "cartesian explosion").
        var order = await orders.QueryNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Warehouse)
            .Include(o => o.Lines).ThenInclude(l => l.Item)
            .Include(o => o.Approvals).ThenInclude(a => a.ActionByUser)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw NotFoundException.For("Order", id);

        return ToDto(order);
    }

    public async Task<OrderDto> GetByNumberAsync(string orderNo, CancellationToken ct = default)
    {
        if (!DocumentNumber.TryParse(orderNo, out var parsed))
            throw new BusinessRuleException($"'{orderNo}' is not a valid order number (expected e.g. SO/2026-27/000042).");

        string normalised = parsed.Value.ToString();
        int id = await orders.QueryNoTracking().Where(o => o.OrderNo == normalised).Select(o => o.Id).FirstOrDefaultAsync(ct);
        return id == 0 ? throw NotFoundException.For("Order", normalised) : await GetByIdAsync(id, ct);
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        if (!await customers.QueryNoTracking().AnyAsync(c => c.Id == request.CustomerId, ct))
            throw new BusinessRuleException($"Customer {request.CustomerId} does not exist.");
        if (!await warehouses.QueryNoTracking().AnyAsync(w => w.Id == request.WarehouseId, ct))
            throw new BusinessRuleException($"Warehouse {request.WarehouseId} does not exist.");

        // ONE query for all items (WHERE Id IN (...)), not one query per line.
        var itemIds = request.Lines.Select(l => l.ItemId).ToList();
        Dictionary<int, Item> itemMap = await items.QueryNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var missing = itemIds.Where(id => !itemMap.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException($"Item(s) not found: {string.Join(", ", missing)}.");
        var inactive = itemMap.Values.Where(i => !i.IsActive).Select(i => i.Code).ToList();
        if (inactive.Count > 0)
            throw new BusinessRuleException($"Inactive item(s) cannot be ordered: {string.Join(", ", inactive)}.");

        DateOnly orderDate = request.OrderDate ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        IPricingStrategy strategy = pricing.For(orderDate);           // Factory picks; we just use the interface
        string orderNo = await procedures.GetNextOrderNoAsync(orderDate, ct);

        var order = Order.Create(orderNo, request.CustomerId, request.WarehouseId, orderDate,
                                 request.Remarks?.Trim(), idempotencyKey);
        foreach (var line in request.Lines)
        {
            Item item = itemMap[line.ItemId];
            decimal discount = strategy.GetDiscountPercent(new PricingContext(orderDate, line.Qty, item.UnitPrice));
            decimal tax = await taxRates.GetTaxPercentAsync(item, ct);
            order.AddLine(item.Id, line.Qty, item.UnitPrice, discount, tax);
        }

        orders.Add(order);
        await uow.SaveChangesAsync(ct);
        return await GetByIdAsync(order.Id, ct);
    }

    public async Task<OrderDto> SubmitAsync(int id, CancellationToken ct = default)
    {
        var order = await LoadForUpdateAsync(id, ct);
        order.Submit(currentUser.UserId, Now);
        await uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    /// <summary>
    /// RESOURCE-BASED AUTHORISATION: the endpoint only checks "can approve at
    /// some level"; which permission is needed depends on the order's current
    /// state, so the service checks it here (403 if missing).
    /// </summary>
    public async Task<OrderDto> ApproveAsync(int id, string? comments, CancellationToken ct = default)
    {
        var order = await LoadForUpdateAsync(id, ct);
        byte level = order.PendingLevel
            ?? throw new ConflictException($"Order {order.OrderNo} is {order.Status}; it is not waiting for approval.");
        EnsureCanActAtLevel(level);

        order.Approve(level, currentUser.UserId, Now, comments);
        await uow.SaveChangesAsync(ct);      // RowVersion: two approvers at once -> second gets 409
        return await GetByIdAsync(id, ct);
    }

    public async Task<OrderDto> RejectAsync(int id, string comments, CancellationToken ct = default)
    {
        var order = await LoadForUpdateAsync(id, ct);
        byte level = order.PendingLevel
            ?? throw new ConflictException($"Order {order.OrderNo} is {order.Status}; only pending orders can be rejected.");
        EnsureCanActAtLevel(level);

        order.Reject(level, currentUser.UserId, Now, comments);
        await uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    /// <summary>Delegates to usp_DespatchOrder: stock, ledger, status and outbox in ONE SQL transaction.</summary>
    public Task<DespatchResultDto> DespatchAsync(int id, CancellationToken ct = default)
        => procedures.DespatchAsync(id, currentUser.UserName, ct);

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var order = await LoadForUpdateAsync(id, ct);
        order.EnsureCanBeDeleted();
        orders.Remove(order);
        await uow.SaveChangesAsync(ct);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;   // TimeProvider: tests can freeze time

    private void EnsureCanActAtLevel(byte level)
    {
        string needed = level == 1 ? PermissionCodes.OrdersApproveL1 : PermissionCodes.OrdersApproveL2;
        if (!currentUser.HasPermission(needed))
            throw new ForbiddenAccessException($"Level-{level} approval requires the '{needed}' permission.");
    }

    private async Task<Order> LoadForUpdateAsync(int id, CancellationToken ct) =>
        await orders.Query().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw NotFoundException.For("Order", id);

    private static OrderDto ToDto(Order o) => new(
        o.Id, o.OrderNo, o.CustomerId, o.Customer.Name, o.WarehouseId, o.Warehouse.Code, o.OrderDate,
        o.Status.ToString(), o.PendingLevel, o.SubTotal, o.TaxAmount, o.TotalAmount, o.Remarks,
        Convert.ToBase64String(o.RowVersion), o.CreatedBy, o.CreatedOn,
        o.Lines.OrderBy(l => l.LineNumber)
               .Select(l => new OrderLineDto(l.LineNumber, l.ItemId, l.Item.Code, l.Item.Name, l.Qty,
                                             l.UnitPrice, l.DiscountPct, l.TaxPercent, l.LineTotal))
               .ToList(),
        o.Approvals.OrderBy(a => a.ActionOn)
                   .Select(a => new ApprovalDto(a.Level, a.Action, a.ActionByUser.UserName, a.ActionOn, a.Comments))
                   .ToList());
}
