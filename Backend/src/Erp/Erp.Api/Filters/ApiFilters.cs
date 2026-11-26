using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Erp.Core.Interfaces;
using Erp.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using MiniErp.Contracts;

namespace Erp.Api.Filters;

/*  FILTERS vs MIDDLEWARE
    Middleware runs for EVERY request and only sees HttpContext.
    Filters run inside MVC around a controller action and know the action:
    its arguments, its result, its attributes. Order around an action:
    Authorization -> Resource -> Action -> (action runs) -> Exception -> Result. */

/// <summary>
/// ACTION FILTER: runs every FluentValidation validator that exists for the
/// action's arguments before the action executes; failures short-circuit
/// with 400 + per-field errors. (Data annotations were already checked by [ApiController].)
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (object? argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());   // reflection: IValidator<CreateItemRequest>
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var group in result.Errors.GroupBy(e => e.PropertyName))
                errors[group.Key] = group.Select(e => e.ErrorMessage).ToArray();
        }

        if (errors.Count > 0)
        {
            context.Result = new BadRequestObjectResult(
                ApiResponse.Fail("Validation failed.", errors, context.HttpContext.TraceIdentifier));
            return;                                  // short-circuit: the action never runs
        }
        await next();
    }
}

/// <summary>Opt an action/controller out of the audit trail (e.g. login: never log passwords).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipAuditAttribute : Attribute;

/// <summary>
/// AUDIT ACTION FILTER: after every successful state-changing call
/// (POST/PUT/PATCH/DELETE) writes who did what to dbo.AuditLog (Source = 'Api').
/// Uses Dapper on its own connection so it never interferes with the action's
/// EF unit of work. (The SQL trigger audits column-level changes on Items; this
/// audits API calls on everything.)
/// </summary>
public sealed class AuditActionFilter(SqlConnectionFactory db, ICurrentUser currentUser, ILogger<AuditActionFilter> logger)
    : IAsyncActionFilter
{
    private static readonly HashSet<string> AuditedMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ActionExecutedContext executed = await next();       // run the action first

        var http = context.HttpContext;
        if (!AuditedMethods.Contains(http.Request.Method)
            || context.ActionDescriptor.EndpointMetadata.OfType<SkipAuditAttribute>().Any()
            || executed.Exception is not null                 // failures are logged by the exception handler
            || !currentUser.IsAuthenticated)
            return;

        int status = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? http.Response.StatusCode;
        if (status >= 400) return;

        try
        {
            string controller = context.RouteData.Values["controller"]?.ToString() ?? "?";
            string action = context.RouteData.Values["action"]?.ToString() ?? "?";
            string recordId = context.RouteData.Values.TryGetValue("id", out var id) ? id?.ToString() ?? "-" : "-";
            string details = JsonSerializer.Serialize(new { http.Request.Method, Path = http.Request.Path.Value, Action = action, Status = status });

            await using var conn = await db.OpenAsync(http.RequestAborted);
            await conn.ExecuteAsync("""
                INSERT INTO dbo.AuditLog (TableName, RecordId, Action, NewValues, ChangedBy, Source)
                VALUES (@Table, @RecordId, @Action, @Details, @User, 'Api');
                """, new
            {
                Table = controller,
                RecordId = recordId,
                Action = http.Request.Method switch { "POST" => "I", "DELETE" => "D", _ => "U" },
                Details = details,
                User = currentUser.UserName
            });
        }
        catch (Exception ex)
        {
            // an audit failure must not fail the user's (already committed) request
            logger.LogError(ex, "Failed to write API audit entry");
        }
    }
}

/// <summary>
/// [Idempotent] for POST endpoints. The client sends an Idempotency-Key header
/// (a GUID). The first response is stored; if the same request is retried (a
/// network timeout, a double click), the stored response is returned instead
/// of creating a second order. Same key + different body = 422.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    public const string HeaderName = "Idempotency-Key";
    public bool IsReusable => false;

    // IFilterFactory: lets an attribute get a filter instance built by DI
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<IdempotencyFilter>(serviceProvider);
}

public sealed class IdempotencyFilter(SqlConnectionFactory db, IOptions<JsonOptions> jsonOptions) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        string? key = http.Request.Headers[IdempotentAttribute.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
        {
            context.Result = new BadRequestObjectResult(ApiResponse.Fail(
                $"The {IdempotentAttribute.HeaderName} header is required (a unique value such as a GUID, max 64 chars).",
                traceId: http.TraceIdentifier));
            return;
        }

        // Hash only the [FromBody] arguments: ActionArguments also holds things like
        // the CancellationToken, which isn't data (and can't be serialised).
        JsonSerializerOptions json = jsonOptions.Value.JsonSerializerOptions;
        var bodyArguments = context.ActionDescriptor.Parameters
            .Where(p => p.BindingInfo?.BindingSource == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Body)
            .Select(p => context.ActionArguments.TryGetValue(p.Name, out var value) ? value : null)
            .ToList();
        string requestHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bodyArguments, json))));
        string path = http.Request.Path.Value ?? "";

        await using (var conn = await db.OpenAsync(http.RequestAborted))
        {
            var existing = await conn.QuerySingleOrDefaultAsync<(string RequestHash, int StatusCode, string? ResponseBody)>(
                "SELECT RequestHash, StatusCode, ResponseBody FROM dbo.IdempotencyRecords WHERE IdempotencyKey = @key;",
                new { key = new DbString { Value = key, IsAnsi = true, Length = 64 } });

            if (existing != default)
            {
                if (existing.RequestHash != requestHash)
                {
                    context.Result = new UnprocessableEntityObjectResult(ApiResponse.Fail(
                        "This Idempotency-Key was already used for a different request.", traceId: http.TraceIdentifier));
                    return;
                }
                http.Response.Headers["Idempotent-Replayed"] = "true";
                context.Result = new ContentResult
                {
                    StatusCode = existing.StatusCode, Content = existing.ResponseBody, ContentType = "application/json"
                };
                return;                                   // replay: the action does not run again
            }
        }

        ActionExecutedContext executed = await next();

        if (executed.Exception is null && executed.Result is ObjectResult { StatusCode: >= 200 and < 300 } ok)
        {
            await using var conn = await db.OpenAsync(http.RequestAborted);
            try
            {
                await conn.ExecuteAsync("""
                    INSERT INTO dbo.IdempotencyRecords (IdempotencyKey, RequestPath, RequestHash, StatusCode, ResponseBody)
                    VALUES (@key, @path, @hash, @status, @body);
                    """, new
                {
                    key = new DbString { Value = key, IsAnsi = true, Length = 64 },
                    path,
                    hash = new DbString { Value = requestHash, IsAnsi = true, Length = 64 },
                    status = ok.StatusCode,
                    body = JsonSerializer.Serialize(ok.Value, json)
                });
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2627 or 2601)
            {
                // two identical requests raced; the first one stored its response. Fine.
            }
        }
    }
}
