namespace Erp.Core.Entities;

public class Role : BaseEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public override string DisplayName => Name;
}

/// <summary>A single permission code such as <c>Orders.Approve.L1</c>. Lookup table, not audited.</summary>
public class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Module { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>Join table for the many-to-many Role ↔ Permission (composite key).</summary>
public class RolePermission
{
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}

public class User : BaseEntity
{
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public int? ManagerId { get; set; }
    public User? Manager { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginOn { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public override string DisplayName => UserName;
}

/// <summary>One login. Only one active session per user is allowed (enforced by a filtered unique index).</summary>
public class UserSession
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime StartedOn { get; set; }
    public DateTime LastActivityOn { get; set; }
    public DateTime? EndedOn { get; set; }
    public string? EndReason { get; set; }

    public bool IsActive => EndedOn is null;

    public void End(string reason, DateTime now)
    {
        if (!IsActive) return;          // idempotent
        EndedOn = now;
        EndReason = reason;
    }
}

public static class SessionEndReasons
{
    public const string Logout = "Logout";
    public const string IdleTimeout = "IdleTimeout";
    public const string NewLogin = "NewLogin";
    public const string Revoked = "Revoked";
}

/// <summary>Only the SHA-256 hash of a refresh token is stored, never the token itself.</summary>
public class RefreshToken
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresOn { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? RevokedOn { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTime now) => RevokedOn is null && ExpiresOn > now;
}
