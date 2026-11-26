namespace Erp.Core.Entities;

public class Customer : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Gstin { get; set; }
    public decimal CreditLimit { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public override string DisplayName => $"{Code} - {Name}";
}

public class Warehouse : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public override string DisplayName => Code;
}

public class ItemCategory : BaseEntity
{
    public string Name { get; set; } = "";
    public override string DisplayName => Name;
}

public class Item : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public ItemCategory Category { get; set; } = null!;
    public string Unit { get; set; } = "NOS";
    public decimal UnitPrice { get; set; }
    public decimal TaxPercent { get; set; } = 18;
    public decimal ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    public override string DisplayName => $"{Code} - {Name}";
}
