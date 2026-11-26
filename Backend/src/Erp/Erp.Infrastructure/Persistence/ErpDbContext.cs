using System.Linq.Expressions;
using Erp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence;

/// <summary>
/// EF Core context over the EXISTING ErpDb schema (database-first: the SQL
/// scripts in Backend/database own the schema; this model maps onto it).
/// Registered as SCOPED: one instance per HTTP request. A DbContext is not
/// thread-safe, so never share one across parallel tasks.
/// </summary>
public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockLedgerEntry> StockLedger => Set<StockLedgerEntry>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<ApprovalHistory> ApprovalHistory => Set<ApprovalHistory>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Defaults for every property of a type; specific configurations override them.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateTime>().HavePrecision(3);     // datetime2(3); temporal period columns need the type name to stay "datetime2"
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration<T> class in this assembly (Configurations/*.cs)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpDbContext).Assembly);
        ApplySoftDeleteFilter(modelBuilder);
    }

    /// <summary>
    /// GLOBAL QUERY FILTER for every ISoftDelete entity: EF adds
    /// "WHERE IsDeleted = 0" to every query automatically
    /// (.IgnoreQueryFilters() switches it off). The lambda e => !e.IsDeleted is
    /// built as an EXPRESSION TREE because the entity type is only known at run time.
    /// </summary>
    private static void ApplySoftDeleteFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ISoftDelete).IsAssignableFrom(t.ClrType) && t.BaseType is null))
        {
            ParameterExpression e = Expression.Parameter(entityType.ClrType, "e");
            Expression body = Expression.Not(Expression.Property(e, nameof(ISoftDelete.IsDeleted)));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, e));
        }
    }
}
