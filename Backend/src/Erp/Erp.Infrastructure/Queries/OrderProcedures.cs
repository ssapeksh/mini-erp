using System.Data;
using Dapper;
using Erp.Core.Dtos;
using Erp.Core.Interfaces;
using Erp.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace Erp.Infrastructure.Queries;

/// <summary>Calls the transactional stored procedures written in Phase 1.</summary>
internal sealed class OrderProcedures(SqlConnectionFactory factory) : IOrderProcedures
{
    public async Task<string> GetNextOrderNoAsync(DateOnly orderDate, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("@OrderDate", orderDate.ToDateTime(TimeOnly.MinValue), DbType.Date);
        p.Add("@OrderNo", dbType: DbType.AnsiString, size: 30, direction: ParameterDirection.Output);

        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("dbo.usp_GetNextOrderNo", p,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return p.Get<string>("@OrderNo");
    }

    /// <summary>
    /// usp_DespatchOrder owns the whole transaction (stock + ledger + status +
    /// outbox). Deadlock victims (1205) are retried; our THROW 50404/50409/50422
    /// become NotFound / Conflict / BusinessRule exceptions -> 404 / 409 / 422.
    /// </summary>
    public async Task<DespatchResultDto> DespatchAsync(int orderId, string despatchedBy, CancellationToken ct = default)
    {
        try
        {
            return await SqlRetry.OnDeadlockAsync(async () =>
            {
                await using var conn = await factory.OpenAsync(ct);
                return await conn.QuerySingleAsync<DespatchResultDto>(new CommandDefinition("dbo.usp_DespatchOrder",
                    new { OrderId = orderId, DespatchedBy = despatchedBy },
                    commandType: CommandType.StoredProcedure, cancellationToken: ct));
            }, ct: ct);
        }
        catch (SqlException ex) when (SqlErrorTranslator.Translate(ex) is { } translated)
        {
            throw translated;
        }
    }
}
