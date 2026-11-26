using Erp.Core.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using MiniErp.Contracts;

namespace Erp.Api.ErrorHandling;

/// <summary>
/// GLOBAL EXCEPTION HANDLING (.NET 8+ IExceptionHandler, used by
/// app.UseExceptionHandler()). Controllers and services just throw; this one
/// place turns every exception into the standard ApiResponse with the right
/// status code, so no action needs try/catch.
/// Unexpected exceptions get a generic message: stack traces and SQL never leak to clients.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        // switch EXPRESSION with type PATTERNS; order matters (most specific first)
        (int status, string message, IReadOnlyDictionary<string, string[]>? errors) = exception switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, "Validation failed.", ToErrors(v)),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message, null),
            ConcurrencyException e => (StatusCodes.Status409Conflict, e.Message, null),
            ConflictException e => (StatusCodes.Status409Conflict, e.Message, null),
            BusinessRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Message, null),
            ForbiddenAccessException e => (StatusCodes.Status403Forbidden, e.Message, null),
            AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, e.Message, null),
            BadHttpRequestException e => (e.StatusCode, "The request was malformed.", null),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested
                => (499, "Request cancelled by the client.", null),
            _ => (StatusCodes.Status500InternalServerError,
                  env.IsDevelopment() ? $"Unexpected error: {exception.Message}" : "Something went wrong. Quote the trace id to support.",
                  null)
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("{Status} {ExceptionType}: {Message}", status, exception.GetType().Name, exception.Message);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(message, errors, context.TraceIdentifier), ct);
        return true;      // handled: don't run other handlers
    }

    private static Dictionary<string, string[]> ToErrors(ValidationException ex) =>
        ex.Errors.GroupBy(e => e.PropertyName)
                 .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
}
