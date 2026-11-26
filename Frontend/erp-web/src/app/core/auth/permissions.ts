/**
 * Permission codes, identical to Erp.Core PermissionCodes / dbo.Permissions.
 * `as const` makes every value a string LITERAL type, so Permission below is
 * the union 'Items.View' | 'Items.Create' | ... and typos don't compile.
 */
export const Permissions = {
  ItemsView: 'Items.View',
  ItemsCreate: 'Items.Create',
  ItemsEdit: 'Items.Edit',
  ItemsDelete: 'Items.Delete',
  OrdersView: 'Orders.View',
  OrdersCreate: 'Orders.Create',
  OrdersApproveL1: 'Orders.Approve.L1',
  OrdersApproveL2: 'Orders.Approve.L2',
  OrdersDespatch: 'Orders.Despatch',
  StockView: 'Stock.View',
  StockAdjust: 'Stock.Adjust',
  ReportsView: 'Reports.View',
  LedgerView: 'Ledger.View',
  UsersManage: 'Users.Manage',
} as const;

export type Permission = (typeof Permissions)[keyof typeof Permissions];
