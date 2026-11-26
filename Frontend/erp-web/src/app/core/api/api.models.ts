/*  TypeScript INTERFACES mirroring the C# DTOs (JSON is camelCase).
    Interfaces exist only at compile time: they type-check the code and
    disappear from the JavaScript bundle.                                  */

/** Every Erp.Api response has this shape (MiniErp.Contracts.ApiResponse<T>). */
export interface ApiResponse<T> {
  success: boolean;
  message?: string | null;
  data?: T | null;
  errors?: Record<string, string[]> | null;
  traceId?: string | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface Lookup {
  id: number;
  code: string;
  name: string;
}

export interface Customer {
  id: number;
  code: string;
  name: string;
  city?: string | null;
  email?: string | null;
  creditLimit: number;
}

// ---------- items ----------
export interface ItemListItem {
  id: number;
  code: string;
  name: string;
  categoryId: number;
  categoryName: string;
  unit: string;
  unitPrice: number;
  taxPercent: number;
  reorderLevel: number;
  isActive: boolean;
  stockQty: number;
  isBelowReorder: boolean;
  rowVersion: string;
}

export interface Item {
  id: number;
  code: string;
  name: string;
  categoryId: number;
  categoryName: string;
  unit: string;
  unitPrice: number;
  taxPercent: number;
  reorderLevel: number;
  isActive: boolean;
  rowVersion: string;
  createdBy: string;
  createdOn: string;
  modifiedBy?: string | null;
  modifiedOn?: string | null;
}

export interface ItemQuery {
  search?: string;
  categoryId?: number | null;
  sortBy: 'Name' | 'Code' | 'Price' | 'Stock';
  sortDir: 'ASC' | 'DESC';
  page: number;
  pageSize: number;
}

export type ItemSaveRequest = Omit<Item, 'id' | 'categoryName' | 'createdBy' | 'createdOn' | 'modifiedBy' | 'modifiedOn' | 'rowVersion' | 'isActive'> & {
  isActive?: boolean;
  rowVersion?: string;
};   // utility types: Omit + intersection

export interface ItemHistory {
  name: string;
  unitPrice: number;
  taxPercent: number;
  isActive: boolean;
  isDeleted: boolean;
  validFrom: string;
  validTo: string;
}

// ---------- orders ----------
export type OrderStatus =
  | 'Draft' | 'PendingL1' | 'PendingL2' | 'Approved' | 'Rejected'
  | 'Despatched' | 'Posted' | 'Failed' | 'Cancelled';   // string literal UNION type

export interface OrderSummary {
  id: number;
  orderNo: string;
  orderDate: string;
  status: OrderStatus;
  customerCode: string;
  customerName: string;
  warehouse: string;
  totalAmount: number;
  lineCount: number;
  createdBy: string;
}

export interface OrderQuery {
  status?: OrderStatus | '';
  search?: string;
  page: number;
  pageSize: number;
}

export interface OrderLine {
  lineNumber: number;
  itemId: number;
  itemCode: string;
  itemName: string;
  qty: number;
  unitPrice: number;
  discountPct: number;
  taxPercent: number;
  lineTotal: number;
}

export interface Approval {
  level: number;
  action: 'Submitted' | 'Approved' | 'Rejected';
  actionBy: string;
  actionOn: string;
  comments?: string | null;
}

export interface Order {
  id: number;
  orderNo: string;
  customerId: number;
  customerName: string;
  warehouseId: number;
  warehouseCode: string;
  orderDate: string;
  status: OrderStatus;
  pendingLevel?: number | null;
  subTotal: number;
  taxAmount: number;
  totalAmount: number;
  remarks?: string | null;
  rowVersion: string;
  createdBy: string;
  createdOn: string;
  lines: OrderLine[];
  approvals: Approval[];
}

export interface CreateOrderRequest {
  customerId: number;
  warehouseId: number;
  orderDate?: string | null;
  remarks?: string | null;
  lines: { itemId: number; qty: number }[];
}

export interface DespatchResult {
  orderId: number;
  orderNo: string;
  status: string;
}

// ---------- stock, reports, admin ----------
export interface StockSummary {
  itemId: number;
  code: string;
  name: string;
  category: string;
  warehouseId: number;
  warehouse: string;
  quantity: number;
  unit: string;
  reorderLevel: number;
  stockStatus: 'OK' | 'Low' | 'Reorder';
  stockValue: number;
}

export interface MonthlySales {
  month: string;
  financialYear: string;
  orders: number;
  total: number;
  prevMonthTotal?: number | null;
  change?: number | null;
  changePct?: number | null;
  fyToDate: number;
}

export interface Dashboard {
  ordersByStatus: { status: OrderStatus; count: number }[];
  lowStockCount: number;
  recentMonths: MonthlySales[];
  topItems: { code: string; name: string; qty: number; amount: number }[];
  elapsedMs: number;
}

export interface Session {
  id: string;
  userId: number;
  userName: string;
  fullName: string;
  ipAddress?: string | null;
  userAgent?: string | null;
  startedOn: string;
  lastActivityOn: string;
  endedOn?: string | null;
  endReason?: string | null;
}
