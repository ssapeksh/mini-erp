using System.Data.Common;
using Erp.Core.Entities;
using Erp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Erp.Infrastructure.Persistence;

/// <summary>
/// Runs just before every SaveChanges and fills the audit columns, so no
/// service ever writes CreatedBy/ModifiedOn by hand. It also turns a hard
/// DELETE of any ISoftDelete entity into "UPDATE ... SET IsDeleted = 1".
/// (Same idea as overriding SaveChanges, or NHibernate event listeners in Phase 5.)
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        string user = currentUser.IsAuthenticated ? currentUser.UserName : "system";

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Soft delete: a Deleted ISoftDelete row becomes an UPDATE
            if (entry is { State: EntityState.Deleted, Entity: ISoftDelete softDelete })
            {
                entry.State = EntityState.Modified;
                softDelete.IsDeleted = true;
            }

            switch (entry.State)
            {
                case EntityState.Added when entry.Entity is ICreationAudited created:
                    created.CreatedBy = user;
                    created.CreatedOn = now;
                    break;
                case EntityState.Modified when entry.Entity is IAuditable audited:
                    audited.ModifiedBy = user;
                    audited.ModifiedOn = now;
                    entry.Property(nameof(ICreationAudited.CreatedBy)).IsModified = false;   // never overwrite creation info
                    entry.Property(nameof(ICreationAudited.CreatedOn)).IsModified = false;
                    break;
            }
        }
    }
}

/// <summary>
/// Every time EF opens a connection, tell SQL Server who the APPLICATION user
/// is. All API calls share one SQL login (minierp_app) and pooled connections,
/// so without this the audit trigger (trg_Items_Audit) could only record
/// "minierp_app". Pooling resets SESSION_CONTEXT, which is why it's set on
/// every open. Cost: one tiny extra round trip per connection.
/// </summary>
public sealed class SessionContextInterceptor(ICurrentUser currentUser) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = CreateCommand(connection);
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
                                                     CancellationToken cancellationToken = default)
    {
        await using var cmd = CreateCommand(connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC sys.sp_set_session_context @key = N'UserName', @value = @user;";
        var p = cmd.CreateParameter();                 // parameterised: user names never become SQL text
        p.ParameterName = "@user";
        p.Value = currentUser.IsAuthenticated ? currentUser.UserName : "system";
        cmd.Parameters.Add(p);
        return cmd;
    }
}
