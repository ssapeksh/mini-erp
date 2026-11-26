using System.Reflection;

namespace Erp.Core.Common;

/// <summary>
/// Permission codes as compile-time constants (<c>const</c>: baked into the
/// calling assembly at compile time, unlike <c>static readonly</c>, which is
/// read at run time). Must match dbo.Permissions.Code.
/// </summary>
public static class PermissionCodes
{
    public const string ItemsView = "Items.View";
    public const string ItemsCreate = "Items.Create";
    public const string ItemsEdit = "Items.Edit";
    public const string ItemsDelete = "Items.Delete";
    public const string OrdersView = "Orders.View";
    public const string OrdersCreate = "Orders.Create";
    public const string OrdersApproveL1 = "Orders.Approve.L1";
    public const string OrdersApproveL2 = "Orders.Approve.L2";
    public const string OrdersDespatch = "Orders.Despatch";
    public const string StockView = "Stock.View";
    public const string StockAdjust = "Stock.Adjust";
    public const string ReportsView = "Reports.View";
    public const string LedgerView = "Ledger.View";
    public const string UsersManage = "Users.Manage";

    /// <summary>
    /// REFLECTION: read every const string declared above at run time.
    /// Lazy&lt;T&gt; so the (slow) reflection runs once, on first use, thread-safely.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<string>> _all = new(() =>
        typeof(PermissionCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(c => c)
            .ToList());

    public static IReadOnlyList<string> All => _all.Value;
}
