using Erp.Core.Common;

namespace Erp.Core.Entities;

public enum OrderStatus
{
    Draft, PendingL1, PendingL2, Approved, Rejected, Despatched, Posted, Failed, Cancelled
}

/// <summary>
/// Sales order. ENCAPSULATION: status and totals have private setters, so the
/// only way to change them is through methods that enforce the workflow
/// (Submit / Approve / Reject). Nobody can set Status = Approved directly.
/// </summary>
public class Order : BaseEntity
{
    private readonly List<OrderLine> _lines = [];
    private readonly List<ApprovalHistory> _approvals = [];

    private Order() { }   // EF Core materialises through this

    public string OrderNo { get; private set; } = "";
    public int CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;
    public int WarehouseId { get; private set; }
    public Warehouse Warehouse { get; private set; } = null!;
    public DateOnly OrderDate { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public decimal SubTotal { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }        // computed column in SQL Server
    public string? Remarks { get; set; }
    public string? IdempotencyKey { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();
    public IReadOnlyCollection<ApprovalHistory> Approvals => _approvals.AsReadOnly();

    public override string DisplayName => OrderNo;

    /// <summary>Factory method: the only way to create a valid order.</summary>
    public static Order Create(string orderNo, int customerId, int warehouseId, DateOnly orderDate,
                               string? remarks, string? idempotencyKey) => new()
    {
        OrderNo = orderNo,
        CustomerId = customerId,
        WarehouseId = warehouseId,
        OrderDate = orderDate,
        Remarks = remarks,
        IdempotencyKey = idempotencyKey,
        Status = OrderStatus.Draft
    };

    public OrderLine AddLine(int itemId, decimal qty, decimal unitPrice, decimal discountPct, decimal taxPercent)
    {
        EnsureStatus(OrderStatus.Draft, "add lines");
        if (qty <= 0) throw new BusinessRuleException("Quantity must be greater than zero.");
        if (_lines.Any(l => l.ItemId == itemId))
            throw new BusinessRuleException($"Item {itemId} is already on this order; change its quantity instead.");

        var line = new OrderLine((short)(_lines.Count + 1), itemId, qty, unitPrice, discountPct, taxPercent);
        _lines.Add(line);
        RecalculateTotals();
        return line;
    }

    /// <summary>Same formula as the SQL computed column, so totals match before and after saving.</summary>
    public void RecalculateTotals()
    {
        var totals = new OrderTotals();
        foreach (var line in _lines)
            OrderTotals.Accumulate(ref totals, line);      // 'ref' struct: no copies in the loop
        SubTotal = totals.SubTotal;
        TaxAmount = Math.Round(totals.Tax, 2);
        TotalAmount = SubTotal + TaxAmount;
    }

    public void Submit(int userId, DateTime now)
    {
        EnsureStatus(OrderStatus.Draft, "submit");
        if (_lines.Count == 0) throw new BusinessRuleException("An order needs at least one line before it can be submitted.");
        Status = OrderStatus.PendingL1;
        _approvals.Add(new ApprovalHistory(0, ApprovalActions.Submitted, userId, now, null));
    }

    /// <summary>Level-1 approval moves to PendingL2; level-2 approval makes the order Approved.</summary>
    public void Approve(byte level, int userId, DateTime now, string? comments)
    {
        var expected = level == 1 ? OrderStatus.PendingL1 : OrderStatus.PendingL2;
        EnsureStatus(expected, $"approve at level {level}");
        Status = level == 1 ? OrderStatus.PendingL2 : OrderStatus.Approved;
        _approvals.Add(new ApprovalHistory(level, ApprovalActions.Approved, userId, now, comments));
    }

    public void Reject(byte level, int userId, DateTime now, string comments)
    {
        if (Status is not (OrderStatus.PendingL1 or OrderStatus.PendingL2))   // pattern matching
            throw new ConflictException($"Order {OrderNo} is {Status}; only pending orders can be rejected.");
        if (string.IsNullOrWhiteSpace(comments))
            throw new BusinessRuleException("A reason is required to reject an order.");
        Status = OrderStatus.Rejected;
        _approvals.Add(new ApprovalHistory(level, ApprovalActions.Rejected, userId, now, comments));
    }

    /// <summary>The approval level the order is waiting for, or null.</summary>
    public byte? PendingLevel => Status switch
    {
        OrderStatus.PendingL1 => 1,
        OrderStatus.PendingL2 => 2,
        _ => null
    };

    public void EnsureCanBeDeleted()
    {
        if (Status != OrderStatus.Draft)
            throw new ConflictException($"Order {OrderNo} is {Status}; only Draft orders can be deleted.");
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
            throw new ConflictException($"Cannot {action}: order {OrderNo} is {Status}, expected {expected}.");
    }
}

public class OrderLine
{
    private OrderLine() { }

    internal OrderLine(short lineNumber, int itemId, decimal qty, decimal unitPrice, decimal discountPct, decimal taxPercent)
    {
        LineNumber = lineNumber;
        ItemId = itemId;
        Qty = qty;
        UnitPrice = unitPrice;
        DiscountPct = discountPct;
        TaxPercent = taxPercent;
        LineTotal = CalculateNet(qty, unitPrice, discountPct);
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public short LineNumber { get; private set; }
    public int ItemId { get; private set; }
    public Item Item { get; private set; } = null!;
    public decimal Qty { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPct { get; private set; }
    public decimal TaxPercent { get; private set; }
    public decimal LineTotal { get; private set; }     // computed column in SQL Server

    public decimal TaxAmount => LineTotal * TaxPercent / 100;

    /// <summary>Mirrors: CAST(Qty * UnitPrice * (100 - DiscountPct) / 100 AS DECIMAL(18,2))</summary>
    public static decimal CalculateNet(decimal qty, decimal unitPrice, decimal discountPct)
        => Math.Round(qty * unitPrice * (100 - discountPct) / 100, 2, MidpointRounding.AwayFromZero);
}

public class ApprovalHistory
{
    private ApprovalHistory() { }

    internal ApprovalHistory(byte level, string action, int actionBy, DateTime actionOn, string? comments)
    {
        Level = level;
        Action = action;
        ActionBy = actionBy;
        ActionOn = actionOn;
        Comments = comments;
    }

    public long Id { get; private set; }
    public int OrderId { get; private set; }
    public byte Level { get; private set; }
    public string Action { get; private set; } = "";
    public int ActionBy { get; private set; }
    public User ActionByUser { get; private set; } = null!;
    public DateTime ActionOn { get; private set; }
    public string? Comments { get; private set; }
}

public static class ApprovalActions
{
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>
/// VALUE TYPE (struct): lives on the stack, copied on assignment. Passed by
/// <c>ref</c> into <see cref="Accumulate"/> so the method updates the caller's
/// copy instead of a temporary one.
/// </summary>
public struct OrderTotals
{
    public decimal SubTotal { get; private set; }
    public decimal Tax { get; private set; }

    public static void Accumulate(ref OrderTotals totals, OrderLine line)
    {
        totals.SubTotal += line.LineTotal;
        totals.Tax += line.TaxAmount;
    }
}
