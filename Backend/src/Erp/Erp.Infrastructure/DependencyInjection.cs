using Erp.Core.Entities;
using Erp.Core.Interfaces;
using Erp.Infrastructure.Caching;
using Erp.Infrastructure.Data;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Queries;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything Infrastructure provides. DI LIFETIMES:
    ///   Singleton - one instance for the whole app: stateless or thread-safe
    ///               (connection factory, token service, caches, trackers)
    ///   Scoped    - one per HTTP request: DbContext and everything that uses it
    ///   Transient - new every time it's resolved (cheap, stateless helpers)
    /// Trap: a Singleton that takes a Scoped service keeps the first request's
    /// instance forever (a "captive dependency"); a DbContext shared across
    /// requests corrupts data. Development mode's ValidateScopes catches it at startup.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>().Bind(configuration.GetSection(JwtSettings.SectionName))
                .Validate(s => s.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters.")
                .ValidateOnStart();                                   // fail fast at startup, not on first login
        services.AddOptions<SessionSettings>().Bind(configuration.GetSection(SessionSettings.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SqlConnectionFactory>();

        // Interceptors depend on ICurrentUser (scoped), so they are scoped too
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddScoped<SessionContextInterceptor>();
        services.AddDbContext<ErpDbContext>((sp, options) =>
        {
            options.UseSqlServer(configuration.GetConnectionString("ErpDb"),
                       sql => sql.CommandTimeout(30))
                   // Required navigations to soft-deletable parents (OrderLine -> Item, ...) are intended:
                   // a row whose parent is soft-deleted is itself history, so hiding it with the parent is correct.
                   .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
                   .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                                    sp.GetRequiredService<SessionContextInterceptor>());
        });                                                           // AddDbContext = Scoped

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));   // open generic
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IItemQueries, ItemQueries>();
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<IStockQueries, StockQueries>();
        services.AddScoped<IReportQueries, ReportQueries>();
        services.AddScoped<IOrderProcedures, OrderProcedures>();

        services.AddMemoryCache();
        services.AddSingleton<LookupCache>();
        services.AddSingleton<ILookupCache>(sp => sp.GetRequiredService<LookupCache>());   // one instance, two service types

        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<SessionActivityTracker>();
        services.AddScoped<AuthService>();
        services.AddScoped<SessionService>();

        return services;
    }
}
