export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export type StockStatusValue = 'normal' | 'low' | 'out';

export interface Product {
  id: number;
  productCode: string;
  name: string;
  description: string | null;
  categoryId: number;
  categoryName: string;
  unitPrice: number;
  currentStock: number;
  minimumStockLevel: number;
  stockStatus: StockStatusValue;
  stockValue: number;
  createdAt: string;
}

export interface Category {
  id: number;
  name: string;
  description: string | null;
  productCount: number;
}

export type TransactionType = 'IN' | 'OUT' | 'ADJUST';

export interface StockTransaction {
  id: number;
  transactionReference: string;
  requestId: string;
  productId: number;
  productCode: string;
  productName: string;
  type: TransactionType;
  quantity: number;
  previousStock: number;
  newStock: number;
  notes: string | null;
  createdAt: string;
}

export interface DailyNetChange {
  date: string;
  netChange: number;
}

export interface Dashboard {
  totalProducts: number;
  totalStockQuantity: number;
  lowStockProducts: number;
  outOfStockProducts: number;
  recentTransactions: StockTransaction[];
  stockChangeLast7Days: DailyNetChange[];
}

export interface UserInfo {
  id: string;
  userName: string;
  roles: string[];
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: UserInfo;
}

export interface ApiErrorBody {
  code: string;
  message: string;
  details: unknown;
  traceId: string;
}

export interface ProductQuery {
  search?: string;
  categoryId?: number | null;
  stockStatus?: string;
  page?: number;
  pageSize?: number;
}

export interface TransactionQuery {
  productId?: number | null;
  type?: string;
  fromUtc?: string;
  toUtc?: string;
  page?: number;
  pageSize?: number;
}

export interface CreateProductPayload {
  productCode: string;
  name: string;
  description?: string | null;
  categoryId: number;
  unitPrice: number;
  currentStock: number;
  minimumStockLevel: number;
}

export interface UpdateProductPayload {
  name: string;
  description?: string | null;
  categoryId: number;
  unitPrice: number;
  minimumStockLevel: number;
}

export interface StockCommandPayload {
  requestId: string;
  productId: number;
  type: TransactionType;
  quantity: number;
  notes?: string | null;
}
