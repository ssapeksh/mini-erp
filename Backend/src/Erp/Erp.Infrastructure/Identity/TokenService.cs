using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Erp.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Erp.Infrastructure.Identity;

/// <summary>Bound from appsettings "Jwt"; the signing key comes from user-secrets / environment, never the repo.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "MiniErp";
    public string Audience { get; set; } = "MiniErp.Clients";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed class SessionSettings
{
    public const string SectionName = "Session";
    public int IdleTimeoutMinutes { get; set; } = 20;
    /// <summary>LastActivityOn is written at most this often per session (saves a DB write per request).</summary>
    public int ActivityWriteIntervalSeconds { get; set; } = 60;
    public int CleanupIntervalSeconds { get; set; } = 60;
}

/// <summary>Custom claim names used across the API.</summary>
public static class ErpClaims
{
    public const string Permission = "perm";
    public const string SessionId = "sid";
    public const string FullName = "full_name";
    public const string Role = "role";
    public const string UserName = JwtRegisteredClaimNames.UniqueName;   // "unique_name"
    public const string UserId = JwtRegisteredClaimNames.Sub;           // "sub"
}

/// <summary>
/// Creates signed JWT access tokens and random refresh tokens. SINGLETON:
/// stateless and thread-safe (only reads options).
/// </summary>
public sealed class JwtTokenService(IOptions<JwtSettings> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresOn) CreateAccessToken(User user, string role, IEnumerable<string> permissions, Guid sessionId)
    {
        JwtSettings s = options.Value;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime expires = now.AddMinutes(s.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ErpClaims.FullName, user.FullName),
            new(ErpClaims.SessionId, sessionId.ToString()),
            new(ErpClaims.Role, role)
        };
        claims.AddRange(permissions.Select(p => new Claim(ErpClaims.Permission, p)));   // one claim per permission

        string token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = s.Issuer,
            Audience = s.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(CreateSigningKey(s.SigningKey), SecurityAlgorithms.HmacSha256)
        });
        return (token, expires);
    }

    public static SymmetricSecurityKey CreateSigningKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters (set it with dotnet user-secrets).");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }

    /// <summary>64 random bytes from a CRYPTOGRAPHIC RNG (never System.Random for secrets).</summary>
    public static string GenerateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    /// <summary>Only the SHA-256 hash is stored: a stolen database can't be used to refresh.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
