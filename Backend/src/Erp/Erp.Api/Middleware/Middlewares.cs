using Erp.Infrastructure.Identity;
using MiniErp.Contracts;
using Serilog.Context;

namespace Erp.Api.Middleware;

/*  CONVENTIONAL MIDDLEWARE: a class with a RequestDelegate 'next' and an
    InvokeAsync(HttpContext) method. It is created ONCE (effectively a
    singleton), so SCOPED services (DbContext, SessionService) must be taken as
    InvokeAsync parameters, never in the constructor (captive dependency).
    Each middleware does work, then calls next(context) to pass the request on;
    not calling next short-circuits the pipeline.                             */

/// <summary>
/// Gives every request an id (from the X-Correlation-Id header or a new one),
/// returns it in the response and adds it to every log line. In Phase 6 the
/// gateway forwards it, so one id follows a request across all services.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers[HeaderName].FirstOrDefault() is { Length: > 0 and <= 64 } incoming
            ? incoming
            : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))   // Serilog enrichment for this request
        {
            await next(context);
        }
    }
}

/// <summary>
/// The JWT alone can't be "logged out": it stays valid until it expires.
/// This middleware checks the session id inside the token against
/// dbo.UserSessions on every request, so logout, a login on another device
/// (single session), idle timeout or an admin revoke take effect IMMEDIATELY.
/// Must run after UseAuthentication (needs the user) and before UseAuthorization.
/// </summary>
public sealed class SessionValidationMiddleware(RequestDelegate next, ILogger<SessionValidationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, SessionService sessions)   // scoped service injected per request
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirst(ErpClaims.SessionId)?.Value, out var sessionId)
            && !await sessions.ValidateAndTouchAsync(sessionId, context.RequestAborted))
        {
            logger.LogInformation("Rejected request for ended session {SessionId}", sessionId);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\", error_description=\"session_ended\"";
            await context.Response.WriteAsJsonAsync(ApiResponse.Fail(
                "Your session has ended (logged out, logged in on another device, idle timeout or revoked). Please log in again.",
                traceId: context.TraceIdentifier));
            return;                                   // short-circuit: next() is NOT called
        }

        await next(context);
    }
}
