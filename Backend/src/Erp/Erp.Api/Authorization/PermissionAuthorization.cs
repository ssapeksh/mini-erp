using Erp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Erp.Api.Authorization;

/*  PERMISSION-BASED RBAC (policy-based authorisation)
    User -> Role -> Permissions. Login puts one "perm" claim per permission
    into the JWT. Endpoints say WHAT they need, not WHO may call them:

        [HasPermission(PermissionCodes.OrdersApproveL1, PermissionCodes.OrdersApproveL2)]

    Role-based [Authorize(Roles = "Admin")] would hard-code roles in code; here
    an admin can give a role a new permission in the database without a deploy.
    Flow: attribute -> policy name "perm:A|B" -> PermissionPolicyProvider builds
    the policy on the fly -> PermissionAuthorizationHandler checks the claims.  */

/// <summary>Requires ANY of the given permissions. Missing => 403 Forbidden (logged in, not allowed).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(params string[] permissions)
    {
        if (permissions.Length == 0) throw new ArgumentException("At least one permission is required.", nameof(permissions));
        Permissions = permissions;
        Policy = PolicyPrefix + string.Join('|', permissions);
    }

    public IReadOnlyList<string> Permissions { get; }
}

public sealed class PermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (requirement.Permissions.Any(p => context.User.HasClaim(ErpClaims.Permission, p)))
            context.Succeed(requirement);
        // not calling Fail(): another handler could still succeed; no success => 403
        return Task.CompletedTask;
    }
}

/// <summary>
/// Creates "perm:..." policies on demand instead of registering one policy per
/// permission combination at startup. Everything else falls back to the default provider.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var permissions = policyName[HasPermissionAttribute.PolicyPrefix.Length..].Split('|');
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()                                  // no token => 401 first
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
