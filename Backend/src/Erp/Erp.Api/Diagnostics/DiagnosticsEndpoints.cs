using Erp.Core.Common;
using Erp.Infrastructure.Caching;
using Erp.Infrastructure.Identity;

namespace Erp.Api.Diagnostics;

/*  DI LIFETIME DEMO (Development only): GET /api/diagnostics/lifetimes
    Call it twice and compare the ids:
      transient: different every time it's resolved, even within one request
      scoped:    same within one request, new in the next request
      singleton: same forever (until the app restarts)                        */

public interface ILifetimeProbe { Guid Id { get; } }
public sealed class TransientProbe : ILifetimeProbe { public Guid Id { get; } = Guid.NewGuid(); }
public sealed class ScopedProbe : ILifetimeProbe { public Guid Id { get; } = Guid.NewGuid(); }
public sealed class SingletonProbe : ILifetimeProbe { public Guid Id { get; } = Guid.NewGuid(); }

/// <summary>A scoped consumer: shows the scoped probe it got is the same one the endpoint got.</summary>
public sealed class ProbeConsumer(TransientProbe transient, ScopedProbe scoped)
{
    public Guid TransientSeenByConsumer => transient.Id;
    public Guid ScopedSeenByConsumer => scoped.Id;
}

public static class DiagnosticsEndpoints
{
    public static IServiceCollection AddLifetimeProbes(this IServiceCollection services) => services
        .AddTransient<TransientProbe>()
        .AddScoped<ScopedProbe>()
        .AddSingleton<SingletonProbe>()
        .AddScoped<ProbeConsumer>();

    public static IEndpointRouteBuilder MapDiagnostics(this IEndpointRouteBuilder app, IHostEnvironment env)
    {
        var group = app.MapGroup("/api/diagnostics").WithTags("Diagnostics");

        // Non-sensitive app info: the Lazy<T> singleton + cache/tracker counters (Interlocked)
        group.MapGet("/info", (LookupCache cache, SessionActivityTracker tracker) => Results.Ok(new
        {
            AppInfo.Instance.Name,
            AppInfo.Instance.Version,
            AppInfo.Instance.Runtime,
            AppInfo.Instance.StartedAtUtc,
            LookupCache = new { cache.Hits, cache.Misses },
            SessionActivityWritesSkipped = tracker.SkippedWrites,
            Permissions = PermissionCodes.All            // discovered by reflection
        })).AllowAnonymous();

        if (env.IsDevelopment())
        {
            group.MapGet("/lifetimes", (TransientProbe t1, TransientProbe t2, ScopedProbe s1, ScopedProbe s2,
                                        SingletonProbe singleton, ProbeConsumer consumer) => Results.Ok(new
            {
                Transient = new { First = t1.Id, Second = t2.Id, InConsumer = consumer.TransientSeenByConsumer, AllDifferent = t1.Id != t2.Id },
                Scoped = new { First = s1.Id, Second = s2.Id, InConsumer = consumer.ScopedSeenByConsumer, SameInThisRequest = s1.Id == s2.Id },
                Singleton = new { singleton.Id, Note = "Same value on every call until restart" }
            })).AllowAnonymous();
        }
        return app;
    }
}
