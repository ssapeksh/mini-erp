namespace Erp.Core.Entities;

/// <summary>Current quantity of one item in one warehouse (composite key ItemId + WarehouseId).</summary>
public class StockBalance
{
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public decimal Quantity { get; set; }
    public DateTime ModifiedOn { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only stock movement (+ in, - out). Only creation-audited.</summary>
public class StockLedgerEntry : ICreationAudited
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime TxnDate { get; set; }
    public string TxnType { get; set; } = "";
    public decimal Qty { get; set; }
    public string? RefDocNo { get; set; }
    public int? RefOrderId { get; set; }
    public string? Remarks { get; set; }
    public string CreatedBy { get; set; } = "system";
    public DateTime CreatedOn { get; set; }
}

public static class StockTxnTypes
{
    public const string Opening = "OPENING";
    public const string Receipt = "RECEIPT";
    public const string Despatch = "DESPATCH";
    public const string Adjustment = "ADJUSTMENT";
    public const string Reversal = "REVERSAL";
}
