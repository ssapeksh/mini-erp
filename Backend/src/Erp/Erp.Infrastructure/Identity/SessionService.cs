using System.Collections.Concurrent;
using Erp.Core.Entities;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Infrastructure.Identity;

/// <summary>
/// SINGLETON in-memory note of when each session's LastActivityOn was last
/// written, so an active user doesn't cause a DB write on every request.
/// ConcurrentDictionary: many requests read/update it in parallel without a lock.
/// </summary>
public sealed class SessionActivityTracker
{
    private readonly ConcurrentDictionary<Guid, DateTime> _lastWritten = new();
    private long _skippedWrites;

    public long SkippedWrites => Interlocked.Read(ref _skippedWrites);

    /// <summary>True if this session's activity should be written now (atomic check-and-set).</summary>
    public bool ShouldWrite(Guid sessionId, DateTime now, TimeSpan interval)
    {
        while (true)
        {
            if (!_lastWritten.TryGetValue(sessionId, out var last))
                return _lastWritten.TryAdd(sessionId, now) || ShouldWrite(sessionId, now, interval);

            if (now - last < interval)
            {
                Interlocked.Increment(ref _skippedWrites);
                return false;
            }
            // only one thread wins the update; losers loop and see the new value
            if (_lastWritten.TryUpdate(sessionId, now, last)) return true;
        }
    }

    public void Forget(Guid sessionId) => _lastWritten.TryRemove(sessionId, out _);
}

/// <summary>Validates the session id carried in the JWT on every request, and ends idle sessions.</summary>
public sealed class SessionService(
    ErpDbContext db,
    SessionActivityTracker tracker,
    IOptions<SessionSettings> options,
    TimeProvider clock)
{
    /// <summary>
    /// True if the session is still active. One statement either way:
    ///  - time to record activity: UPDATE ... WHERE Id = @id AND EndedOn IS NULL
    ///    (0 rows updated = session ended, so the same statement validates)
    ///  - otherwise: a primary-key EXISTS check.
    /// </summary>
    public async Task<bool> ValidateAndTouchAsync(Guid sessionId, CancellationToken ct = default)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        var interval = TimeSpan.FromSeconds(options.Value.ActivityWriteIntervalSeconds);

        if (tracker.ShouldWrite(sessionId, now, interval))
        {
            int updated = await db.UserSessions
                .Where(s => s.Id == sessionId && s.EndedOn == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityOn, now), ct);
            if (updated == 0) tracker.Forget(sessionId);
            return updated > 0;
        }

        return await db.UserSessions.AnyAsync(s => s.Id == sessionId && s.EndedOn == null, ct);
    }

    /// <summary>Called by the IdleSessionLogoutService background job.</summary>
    public async Task<int> EndIdleSessionsAsync(CancellationToken ct = default)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime cutoff = now.AddMinutes(-options.Value.IdleTimeoutMinutes);

        var idleIds = await db.UserSessions
            .Where(s => s.EndedOn == null && s.LastActivityOn < cutoff)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (idleIds.Count == 0) return 0;

        await db.UserSessions
            .Where(s => idleIds.Contains(s.Id) && s.EndedOn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedOn, now)
                                      .SetProperty(x => x.EndReason, SessionEndReasons.IdleTimeout), ct);
        await db.RefreshTokens
            .Where(t => t.SessionId != null && idleIds.Contains(t.SessionId.Value) && t.RevokedOn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedOn, now), ct);

        foreach (var id in idleIds) tracker.Forget(id);
        return idleIds.Count;
    }
}
