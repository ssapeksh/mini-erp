using System.Data.Common;
using Erp.Core.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Erp.Infrastructure.Data;

/// <summary>Creates ADO.NET connections for Dapper. SINGLETON: holds only the connection string.</summary>
public sealed class SqlConnectionFactory(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("ErpDb")
        ?? throw new InvalidOperationException(
            "Connection string 'ErpDb' is missing. Run Backend/scripts/set-dev-secrets.ps1 (dotnet user-secrets).");

    /// <summary>
    /// The caller disposes it with 'await using': Dispose returns the
    /// connection to the POOL (it doesn't really close the socket).
    /// </summary>
    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}

/// <summary>
/// Maps SQL Server error numbers to domain exceptions (which the API maps to HTTP).
/// The 5xxxx numbers are our own THROWs from usp_DespatchOrder (Phase 1).
/// </summary>
public static class SqlErrorTranslator
{
    public const int Deadlock = 1205;

    public static AppException? Translate(SqlException ex) => ex.Number switch
    {
        50404 => new NotFoundException(ex.Message),
        50409 => new ConflictException(ex.Message, ex),
        50422 => new BusinessRuleException(ex.Message, ex),
        2601 or 2627 => new ConflictException("A record with the same unique value already exists.", ex),  // unique index / constraint
        547 => new BusinessRuleException("The change breaks a data rule (CHECK or FOREIGN KEY constraint).", ex),
        _ => null
    };
}

/// <summary>
/// Retries an operation that lost a DEADLOCK (error 1205). SQL Server rolls
/// the victim back completely, so running it again is safe.
/// Func&lt;Task&lt;T&gt;&gt; is a DELEGATE: the caller passes "the work" as a lambda.
/// </summary>
public static class SqlRetry
{
    public static async Task<T> OnDeadlockAsync<T>(Func<Task<T>> operation, int maxAttempts = 3, CancellationToken ct = default)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // ConfigureAwait(false): library code doesn't need to resume on the caller's context.
                // (ASP.NET Core has no SynchronizationContext, so it matters most in UI/legacy apps.)
                return await operation().ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number == SqlErrorTranslator.Deadlock && attempt < maxAttempts)
            {
                // exponential backoff with jitter: 100ms, 200ms, ... + random
                var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 50));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }
}
