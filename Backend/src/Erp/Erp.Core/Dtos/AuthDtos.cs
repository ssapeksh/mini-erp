using System.ComponentModel.DataAnnotations;

namespace Erp.Core.Dtos;

public sealed record LoginRequest(
    [Required, StringLength(50)] string UserName,
    [Required, StringLength(100)] string Password);

public sealed record RefreshRequest([Required] string RefreshToken);

public sealed record UserInfoDto(int Id, string UserName, string FullName, string Role, IReadOnlyList<string> Permissions);

public sealed record AuthResponse(
    string AccessToken, DateTime AccessTokenExpiresOn,
    string RefreshToken, DateTime RefreshTokenExpiresOn,
    Guid SessionId, UserInfoDto User);

public sealed record SessionDto(
    Guid Id, int UserId, string UserName, string FullName, string? IpAddress, string? UserAgent,
    DateTime StartedOn, DateTime LastActivityOn, DateTime? EndedOn, string? EndReason);

/// <summary>Who is calling and from where (passed from the HTTP layer to the auth service).</summary>
public sealed record ClientInfo(string? IpAddress, string? UserAgent);
