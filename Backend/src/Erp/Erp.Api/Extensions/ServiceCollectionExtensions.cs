using System.Threading.RateLimiting;
using Erp.Api.Authorization;
using Erp.Api.Common;
using Erp.Api.Controllers;
using Erp.Core.Interfaces;
using Erp.Core.Pricing;
using Erp.Core.Services;
using Erp.Core.Validators;
using Erp.Infrastructure.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MiniErp.Contracts;

namespace Erp.Api.Extensions;

/// <summary>Keeps Program.cs readable: each concern registers itself in one method.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Business layer (Erp.Core) registrations.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddScoped<ItemService>();
        services.AddScoped<OrderService>();
        services.AddScoped<StockService>();

        services.Configure<PricingOptions>(config.GetSection(PricingOptions.SectionName));   // OPTIONS pattern
        services.AddSingleton<StandardPricingStrategy>();
        services.AddSingleton<FestivalPricingStrategy>();
        services.AddSingleton<IPricingStrategyFactory, PricingStrategyFactory>();
        services.AddSingleton<ITaxRateProvider, ItemMasterTaxRateProvider>();   // Phase 5 swaps this for the WCF client

        // Scans Erp.Core for AbstractValidator<T> classes; validators are stateless -> transient is fine
        services.AddValidatorsFromAssemblyContaining<CreateItemRequestValidator>(ServiceLifetime.Transient);
        return services;
    }

    /// <summary>
    /// Two authentication SCHEMES:
    ///   JwtBearer (default) - the REST API used by Angular
    ///   LegacyCookie        - the server-rendered MVC/jQuery pages
    /// Both 401/403 responses return the standard ApiResponse JSON.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;     // keep "sub", "perm", "sid" as written (no long URI claim types)
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.SigningKey),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),           // default is 5 minutes: too lenient
                    NameClaimType = ErpClaims.UserName,
                    RoleClaimType = ErpClaims.Role
                };
                options.Events = new JwtBearerEvents
                {
                    // 401: not logged in / token missing, expired or invalid
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();                   // replace the default empty 401
                        bool expired = context.AuthenticateFailure is SecurityTokenExpiredException;
                        if (expired) context.Response.Headers["Token-Expired"] = "true";   // Angular interceptor -> refresh
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(
                            expired ? "Access token expired." : "Authentication required.",
                            traceId: context.HttpContext.TraceIdentifier));
                    },
                    // 403: logged in, but missing the permission
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(
                            "You don't have permission to do this.", traceId: context.HttpContext.TraceIdentifier));
                    }
                };
            })
            .AddCookie(LegacyAuth.Scheme, options =>
            {
                options.LoginPath = "/legacy/login";
                options.AccessDeniedPath = "/legacy/login";
                options.Cookie.Name = "minierp.legacy";
                options.Cookie.HttpOnly = true;                     // JavaScript can't read it (XSS can't steal it)
                options.Cookie.SameSite = SameSiteMode.Strict;      // extra CSRF defence on top of anti-forgery tokens
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.SlidingExpiration = true;
            });

        services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");   // jQuery sends the token in this header
        return services;
    }

    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }

    /// <summary>One OpenAPI document per API version, shown in Swagger UI with a JWT "Authorize" button.</summary>
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        foreach (string version in new[] { "v1", "v2" })
        {
            services.AddOpenApi(version, options =>
            {
                options.AddDocumentTransformer((document, _, _) =>
                {
                    document.Info = new OpenApiInfo
                    {
                        Title = "MiniErp API",
                        Version = version,
                        Description = "Items, orders, stock and reports. Log in with POST /api/v1/auth/login "
                                      + "(e.g. admin / Demo@123), then click Authorize and paste the accessToken."
                    };
                    return Task.CompletedTask;
                });
                options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            });
        }
        return services;
    }

    /// <summary>
    /// RATE LIMITING: "login" = 5 attempts per minute per IP (brute force);
    /// global = 100 requests per 10 s per user (or IP when anonymous).
    /// Rejected requests get 429 + Retry-After.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        int loginPermit = config.GetValue("RateLimiting:LoginPermitPerMinute", 5);
        int globalPermit = config.GetValue("RateLimiting:GlobalPermitPer10Seconds", 100);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermit, Window = TimeSpan.FromMinutes(1) }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: http.User.Identity?.Name ?? http.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = globalPermit,
                        TokensPerPeriod = globalPermit,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                await context.HttpContext.Response.WriteAsJsonAsync(ApiResponse.Fail(
                    "Too many requests. Please wait and try again.", traceId: context.HttpContext.TraceIdentifier), ct);
            };
        });
        return services;
    }

    /// <summary>
    /// OUTPUT CACHE policies. The default policy never caches requests that carry
    /// an Authorization header (responses might be user-specific). Lookups and
    /// these reports are the same for every user, so their policies opt in
    /// (excludeDefaultPolicy: true) and cache them anyway.
    /// </summary>
    public static IServiceCollection AddApiCaching(this IServiceCollection services)
    {
        services.AddOutputCache(options =>
        {
            options.AddPolicy("lookups", b => b.Cache().Expire(TimeSpan.FromMinutes(5)).SetVaryByQuery("*").Tag("lookups"),
                excludeDefaultPolicy: true);
            options.AddPolicy("reports", b => b.Cache().Expire(TimeSpan.FromSeconds(60)).SetVaryByQuery("*").Tag("reports"),
                excludeDefaultPolicy: true);
        });
        services.AddResponseCaching();
        return services;
    }
}

/// <summary>Adds the "Bearer" security scheme so Swagger UI shows an Authorize button.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the accessToken from POST /api/v1/auth/login"
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
        return Task.CompletedTask;
    }
}
