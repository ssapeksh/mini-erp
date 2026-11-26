using Erp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence.Configurations;

/*  FLUENT API mappings onto the existing tables (Backend/database/01_schema.sql).
    IsUnicode(false) on every VARCHAR column matters for performance: without it
    EF sends nvarchar parameters, SQL Server converts the COLUMN
    (CONVERT_IMPLICIT) and the index seek becomes a scan. See tuning-lab step 3. */

internal static class MappingExtensions
{
    /// <summary>Audit + soft-delete columns shared by every BaseEntity.</summary>
    public static void MapBaseEntity<T>(this EntityTypeBuilder<T> b) where T : BaseEntity
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.ModifiedBy).HasMaxLength(100);
        b.Ignore(x => x.DisplayName);
    }

    /// <summary>
    /// VARCHAR(n). GENERIC so one method serves string, string? and an enum
    /// stored as text (OrderStatus); the compiler infers T from the call.
    /// </summary>
    public static PropertyBuilder<T> Varchar<T>(this PropertyBuilder<T> p, int length) =>
        p.HasMaxLength(length).IsUnicode(false);
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.MapBaseEntity();
        b.Property(x => x.Name).HasMaxLength(50);
        b.Property(x => x.Description).HasMaxLength(200);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.Property(x => x.Code).Varchar(100);
        b.Property(x => x.Module).Varchar(50);
        b.Property(x => x.Description).HasMaxLength(200);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(x => new { x.RoleId, x.PermissionId });            // composite key
        b.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId);
        b.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.MapBaseEntity();
        b.Property(x => x.UserName).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.FullName).HasMaxLength(100);
        b.Property(x => x.PasswordHash).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        b.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId);     // self reference
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("UserSessions");
        b.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");   // DB generates the GUID
        b.Property(x => x.IpAddress).Varchar(45);
        b.Property(x => x.UserAgent).HasMaxLength(300);
        b.Property(x => x.EndReason).Varchar(30);
        b.Ignore(x => x.IsActive);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.Property(x => x.TokenHash).Varchar(128);
        b.Property(x => x.ReplacedByTokenHash).Varchar(128);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.MapBaseEntity();
        b.Property(x => x.Code).Varchar(20);
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.Gstin).HasColumnName("GSTIN").HasMaxLength(15).IsFixedLength().IsUnicode(false);  // CHAR(15)
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("Warehouses");
        b.MapBaseEntity();
        b.Property(x => x.Code).Varchar(10);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.City).HasMaxLength(80);
    }
}

internal sealed class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> b)
    {
        b.ToTable("ItemCategories");
        b.MapBaseEntity();
        b.Property(x => x.Name).HasMaxLength(100);
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("Items", tb =>
        {
            // TEMPORAL TABLE: lets EF query history with TemporalAll()/TemporalAsOf()
            tb.IsTemporal(t =>
            {
                t.HasPeriodStart("ValidFrom");
                t.HasPeriodEnd("ValidTo");
                t.UseHistoryTable("ItemsHistory");
            });
            // The table has a trigger: EF must not use "OUTPUT INSERTED.*" without INTO,
            // which SQL Server forbids on tables with triggers.
            tb.HasTrigger("trg_Items_Audit");
        });
        b.MapBaseEntity();
        b.Property(x => x.Code).Varchar(30);
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Unit).Varchar(10);
        b.Property(x => x.TaxPercent).HasPrecision(5, 2);
        b.Property(x => x.ReorderLevel).HasPrecision(18, 3);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("StockBalances");
        b.HasKey(x => new { x.ItemId, x.WarehouseId });
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId);
    }
}

internal sealed class StockLedgerConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> b)
    {
        b.ToTable("StockLedger");
        b.Property(x => x.TxnType).Varchar(20);
        b.Property(x => x.Qty).HasPrecision(18, 3);
        b.Property(x => x.RefDocNo).Varchar(30);
        b.Property(x => x.Remarks).HasMaxLength(200);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("Orders");
        b.MapBaseEntity();
        b.Property(x => x.OrderNo).Varchar(30);
        b.Property(x => x.Status).HasConversion<string>().Varchar(20);   // enum stored as its name
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.IdempotencyKey).Varchar(64);
        b.Property(x => x.TotalAmount).HasComputedColumnSql("[SubTotal] + [TaxAmount]", stored: true);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.PendingLevel);

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId);

        // Read-only collections backed by private fields (encapsulation)
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Approvals).WithOne().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Approvals).HasField("_approvals").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> b)
    {
        b.ToTable("OrderLines");
        b.Property(x => x.Qty).HasPrecision(18, 3);
        b.Property(x => x.DiscountPct).HasPrecision(5, 2);
        b.Property(x => x.TaxPercent).HasPrecision(5, 2);
        b.Property(x => x.LineTotal)
            .HasComputedColumnSql("CAST([Qty] * [UnitPrice] * (100 - [DiscountPct]) / 100 AS DECIMAL(18,2))", stored: true);
        b.Ignore(x => x.TaxAmount);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
    }
}

internal sealed class ApprovalHistoryConfiguration : IEntityTypeConfiguration<ApprovalHistory>
{
    public void Configure(EntityTypeBuilder<ApprovalHistory> b)
    {
        b.ToTable("ApprovalHistory");
        b.Property(x => x.Action).Varchar(10);
        b.Property(x => x.Comments).HasMaxLength(500);
        b.HasOne(x => x.ActionByUser).WithMany().HasForeignKey(x => x.ActionBy);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("AuditLog");
        b.Property(x => x.TableName).HasMaxLength(128);
        b.Property(x => x.RecordId).HasMaxLength(50);
        b.Property(x => x.Action).HasMaxLength(1).IsFixedLength().IsUnicode(false);
        b.Property(x => x.ChangedBy).HasMaxLength(100);
        b.Property(x => x.Source).Varchar(20);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages");
        b.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.MessageType).Varchar(200);
        b.Property(x => x.LastError).HasMaxLength(1000);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords");
        b.HasKey(x => x.IdempotencyKey);
        b.Property(x => x.IdempotencyKey).Varchar(64);
        b.Property(x => x.RequestPath).HasMaxLength(200);
        b.Property(x => x.RequestHash).Varchar(64);
    }
}
