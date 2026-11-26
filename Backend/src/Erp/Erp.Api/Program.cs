using Asp.Versioning;
using Erp.Api.BackgroundServices;
using Erp.Api.Diagnostics;
using Erp.Api.Endpoints;
using Erp.Api.ErrorHandling;
using Erp.Api.Extensions;
using Erp.Api.Filters;
using Erp.Api.Middleware;
using Erp.Infrastructure;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using MiniErp.Contracts;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// LOGGING: Serilog, structured ("{UserName}" stays a field, not just text),
// enriched with the CorrelationId pushed by CorrelationIdMiddleware.
// ---------------------------------------------------------------------------
builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}"));

// ---------------------------------------------------------------------------
// SERVICES (dependency injection container)
// ---------------------------------------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration);        // EF Core, Dapper, auth services
builder.Services.AddApplicationServices(builder.Configuration);   // business services, validators, pricing
builder.Services.AddApiAuthentication(builder.Configuration);     // JWT + legacy cookie
builder.Services.AddPermissionAuthorization();                    // [HasPermission] policies
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddApiCaching();
builder.Services.AddApiDocumentation();
builder.Services.AddLifetimeProbes();

builder.Services
    .AddControllersWithViews(options =>
    {
        options.Filters.Add<FluentValidationFilter>();   // global filters, resolved from DI per request
        options.Filters.Add<AuditActionFilter>();
        options.ReturnHttpNotAcceptable = true;          // CONTENT NEGOTIATION: Accept: application/xml -> 406
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // [ApiController] automatic 400 -> our ApiResponse shape instead of ProblemDetails
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(kv => kv.Value?.Errors.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
            return new BadRequestObjectResult(ApiResponse.Fail("Validation failed.", errors, context.HttpContext.TraceIdentifier));
        };
    });

// API VERSIONING: /api/v1/..., /api/v2/... (or the X-Api-Version header)
builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;                // response header: api-supported-versions
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";                // groups "v1", "v2" = OpenAPI document names
        options.SubstituteApiVersionInUrl = true;
    });

// CORS: the Angular dev server (another origin) may call this API
string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("ETag", "Location", "Token-Expired", "Retry-After", "Idempotent-Replayed",
                        "Content-Disposition", CorrelationIdMiddleware.HeaderName, "api-supported-versions")));

builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ErpDbContext>("erpdb", tags: ["ready"]);          // runs a quick query against SQL Server
builder.Services.AddHostedService<IdleSessionLogoutService>();          // background job

var app = builder.Build();

// ---------------------------------------------------------------------------
// MIDDLEWARE PIPELINE: ORDER MATTERS. A request flows top to bottom, the
// response flows back bottom to top.
// ---------------------------------------------------------------------------
app.UseMiddleware<CorrelationIdMiddleware>();       // 1. id first, so every later log line carries it
app.UseSerilogRequestLogging();                     // 2. one log line per request; OUTSIDE the exception handler
                                                    //    so it logs the status the client really got (401, not 500)
app.UseExceptionHandler();                          // 3. catches exceptions from everything below

app.Use(async (context, next) =>                     // 4. inline middleware: security headers
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);                             //    then hand over to the next component
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                                // /openapi/v1.json, /openapi/v2.json
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "MiniErp API v1");
        options.SwaggerEndpoint("/openapi/v2.json", "MiniErp API v2");
        options.RoutePrefix = "swagger";
    });
}
else
{
    app.UseHsts();                                   // production: force HTTPS in browsers
    app.UseHttpsRedirection();
}

app.UseResponseCompression();                       // 5. gzip/brotli responses
app.UseStaticFiles();                               // 6. wwwroot (legacy js/css, playground): short-circuits, no auth needed
app.UseRouting();                                   // 7. pick the endpoint
app.UseCors("Frontend");                            // 8. after routing, before auth (preflight OPTIONS answered here)
app.UseRateLimiter();                               // 9. after routing: endpoint-specific policies ("login")
app.UseAuthentication();                            // 10. who are you? (JWT or cookie -> HttpContext.User)
app.UseMiddleware<SessionValidationMiddleware>();   // 11. is your session still active?
app.UseAuthorization();                             // 12. may you do this? ([Authorize], [HasPermission])
app.UseResponseCaching();
app.UseOutputCache();                               // 13. after auth, so cached endpoints are still protected

// ---------------------------------------------------------------------------
// ENDPOINTS
// ---------------------------------------------------------------------------
app.MapControllers();                               // attribute-routed API + legacy MVC controllers
app.MapLookupEndpoints();                           // minimal API
app.MapDiagnostics(app.Environment);
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });       // process is up
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });   // DB reachable
app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/swagger" : "/health/ready")).ExcludeFromDescription();

await app.RunAsync();

/// <summary>Makes the auto-generated Program class public so WebApplicationFactory&lt;Program&gt; can reach it in Phase 4.</summary>
public partial class Program
{
    protected Program() { }
}
