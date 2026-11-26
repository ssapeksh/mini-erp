using Asp.Versioning;
using Erp.Api.Filters;
using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Interfaces;
using Erp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MiniErp.Contracts;

namespace Erp.Api.Controllers;

/// <summary>Login / refresh / logout. See AuthService for the full JWT flow.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[SkipAudit]                                       // never write request bodies containing passwords to the audit log
public sealed class AuthController(AuthService auth, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Returns an access token (15 min) + refresh token (7 days). Rate limited: 5 attempts/minute per IP.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]                 // brute-force protection
    [ProducesResponseType<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ClientInfo(), ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result, $"Welcome, {result.User.FullName}."));
    }

    /// <summary>Rotates the refresh token: the old one stops working; reusing it ends the session.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh([FromBody] RefreshRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AuthResponse>.Ok(await auth.RefreshAsync(request.RefreshToken, ct)));

    /// <summary>Ends the session: the access token is rejected from now on (SessionValidationMiddleware).</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> Logout(CancellationToken ct)
    {
        if (currentUser.SessionId is { } sessionId) await auth.LogoutAsync(sessionId, ct);
        return Ok(ApiResponse.Message("Logged out."));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserInfoDto>>> Me(CancellationToken ct) =>
        Ok(ApiResponse<UserInfoDto>.Ok(await auth.GetUserInfoAsync(currentUser.UserId, ct)));

    private ClientInfo ClientInfo() => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString());
}

/// <summary>Session Insight: who is logged in, and the power to kick a session out.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/sessions")]
[Authorization.HasPermission(PermissionCodes.UsersManage)]
public sealed class AdminSessionsController(AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SessionDto>>>> Get([FromQuery] bool activeOnly = true, CancellationToken ct = default) =>
        Ok(ApiResponse<IReadOnlyList<SessionDto>>.Ok(await auth.GetSessionsAsync(activeOnly, ct)));

    /// <summary>Force-logout: the user's very next request gets 401.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke([FromRoute] Guid id, CancellationToken ct) =>
        await auth.EndSessionAsync(id, Core.Entities.SessionEndReasons.Revoked, ct)
            ? NoContent()
            : NotFound(ApiResponse.Fail($"No active session {id}."));
}
