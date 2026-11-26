using Erp.Core.Interfaces;
using Erp.Infrastructure.Identity;

namespace Erp.Api.Common;

/// <summary>
/// ICurrentUser from the JWT (or legacy cookie) claims of the current request.
/// SCOPED: one per request. Outside a request (background jobs) there's no
/// HttpContext, so it reports an unauthenticated "system" user.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public int UserId => int.TryParse(User?.FindFirst(ErpClaims.UserId)?.Value, out var id) ? id : 0;

    public string UserName => User?.FindFirst(ErpClaims.UserName)?.Value ?? "system";

    public Guid? SessionId => Guid.TryParse(User?.FindFirst(ErpClaims.SessionId)?.Value, out var sid) ? sid : null;

    public IReadOnlyCollection<string> Permissions =>
        User?.FindAll(ErpClaims.Permission).Select(c => c.Value).ToArray() ?? [];

    public bool HasPermission(string permissionCode) => User?.HasClaim(ErpClaims.Permission, permissionCode) == true;
}
