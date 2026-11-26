using Erp.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Erp.Api.BackgroundServices;

/// <summary>
/// HOSTED SERVICE (BackgroundService): runs for the lifetime of the app and,
/// every minute, ends sessions with no activity for 20 minutes (idle-user logout).
///
/// A hosted service is a SINGLETON, but SessionService uses the scoped
/// DbContext. So it must NOT take SessionService in its constructor (that
/// would be a captive dependency). It creates its own SCOPE per run instead.
/// </summary>
public sealed class IdleSessionLogoutService(
    IServiceScopeFactory scopeFactory,
    IOptions<SessionSettings> options,
    ILogger<IdleSessionLogoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, options.Value.CleanupIntervalSeconds));
        using var timer = new PeriodicTimer(interval);      // no drift, no overlapping runs

        // 'async Task', never 'async void': exceptions in async void can't be caught and crash the process
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
                int ended = await sessions.EndIdleSessionsAsync(stoppingToken);
                if (ended > 0)
                    logger.LogInformation("Idle logout: ended {Count} session(s) idle for {Minutes}+ minutes",
                        ended, options.Value.IdleTimeoutMinutes);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;                                                // app shutting down
            }
            catch (Exception ex)
            {
                // one failed run (e.g. DB restarting) must not kill the background loop
                logger.LogError(ex, "Idle session cleanup failed; will retry next tick");
            }
        }
    }
}
