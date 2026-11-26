using System.Security.Claims;
using Dapper;
using Erp.Api.Authorization;
using Erp.Api.Filters;
using Erp.Core.Common;
using Erp.Core.Dtos;
using Erp.Core.Interfaces;
using Erp.Core.Services;
using Erp.Infrastructure.Data;
using Erp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>
/// A small "legacy" MVC area like an older ASP.NET MVC + jQuery app:
///  - Controller (with views) instead of ControllerBase
///  - COOKIE authentication (a second auth scheme next to JWT)
///  - because the browser sends cookies automatically, POSTs are open to CSRF,
///    so every POST checks an ANTI-FORGERY token ([ValidateAntiForgeryToken]);
///    jQuery sends it in the RequestVerificationToken header.
///    (JWT APIs don't need this: the browser never attaches a Bearer token by itself.)
/// </summary>
[Route("legacy")]
[Authorize(AuthenticationSchemes = LegacyAuth.Scheme)]
[ApiExplorerSettings(IgnoreApi = true)]           // keep MVC pages out of Swagger
[SkipAudit]
public sealed class LegacyController(AuthService auth, StockService stock, SqlConnectionFactory db, ICurrentUser currentUser)
    : Controller
{
    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(StockReport));

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LegacyLoginModel { ReturnUrl = returnUrl });

    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LegacyLoginModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var user = await auth.VerifyCredentialsAsync(new LoginRequest(model.UserName, model.Password),
                new ClientInfo(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString()), ct);

            // Same claim names as the JWT, so [HasPermission] and ICurrentUser work for both schemes
            var claims = new List<Claim>
            {
                new(ErpClaims.UserId, user.Id.ToString()),
                new(ErpClaims.UserName, user.UserName),
                new(ErpClaims.FullName, user.FullName),
                new(ErpClaims.Role, user.Role)
            };
            claims.AddRange(user.Permissions.Select(p => new Claim(ErpClaims.Permission, p)));
            var identity = new ClaimsIdentity(claims, LegacyAuth.Scheme, ErpClaims.UserName, ErpClaims.Role);

            await HttpContext.SignInAsync(LegacyAuth.Scheme, new ClaimsPrincipal(identity));
            return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction(nameof(StockReport));
        }
        catch (AuthenticationFailedException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(LegacyAuth.Scheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("stock-report")]
    [HasPermission(PermissionCodes.StockView)]
    public IActionResult StockReport() => View();

    /// <summary>JSON for the jQuery $.ajax GET.</summary>
    [HttpGet("stock-data")]
    [HasPermission(PermissionCodes.StockView)]
    public async Task<IActionResult> StockData([FromQuery] int? warehouseId, CancellationToken ct) =>
        Json(await stock.GetSummaryAsync(warehouseId, null, ct));

    /// <summary>jQuery $.ajax POST with the anti-forgery header. Records a reorder request in the audit log.</summary>
    [HttpPost("reorder-request")]
    [HasPermission(PermissionCodes.StockView)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderRequest([FromBody] ReorderRequestModel model, CancellationToken ct)
    {
        if (model.Qty <= 0) return BadRequest(new { success = false, message = "Quantity must be greater than zero." });

        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO dbo.AuditLog (TableName, RecordId, Action, NewValues, ChangedBy, Source)
            VALUES ('ReorderRequest', @RecordId, 'I',
                    (SELECT @ItemId AS itemId, @WarehouseId AS warehouseId, @Qty AS qty, @Note AS note FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                    @User, 'Api');
            """, new { RecordId = $"{model.ItemId}/{model.WarehouseId}", model.ItemId, model.WarehouseId, model.Qty, model.Note, User = currentUser.UserName });

        return Json(new { success = true, message = $"Reorder request for {model.Qty:0.###} unit(s) recorded." });
    }
}

public static class LegacyAuth
{
    public const string Scheme = "LegacyCookie";
}

public sealed class LegacyLoginModel
{
    [System.ComponentModel.DataAnnotations.Required] public string UserName { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Required] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public sealed record ReorderRequestModel(int ItemId, int WarehouseId, decimal Qty, string? Note);
