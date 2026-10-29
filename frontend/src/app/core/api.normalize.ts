import {
  Category,
  DailyNetChange,
  Dashboard,
  LoginResponse,
  PagedResult,
  Product,
  StockStatusValue,
  StockTransaction,
  TransactionType,
  UserInfo,
} from './api.models';

/**
 * 后端响应归一化层。
 *
 * 存在两套后端实现，接口契约有差异：
 *   - src/Inventory.Api           —— 契约与前端模型 1:1（扁平仪表盘、stockValue、roles[]）
 *   - deliverables/P03_delivery   —— 成员 A/B 交付版（嵌套仪表盘、role 单值、expiresIn 秒）
 *
 * 本文件把两者都收敛到 api.models.ts 定义的前端模型，页面组件因此完全不需要感知
 * 后端是哪一套。新增后端字段变化时，只改这里。
 */

/* ------------------------------------------------------------------ */
/* 原始响应类型（宽松定义，只描述我们真正读取的字段）                    */
/* ------------------------------------------------------------------ */

export interface RawUserInfo {
  id?: string | number;
  userName?: string;
  username?: string;
  roles?: string[];
  role?: string;
}

export interface RawLoginResponse {
  accessToken?: string;
  tokenType?: string;
  /** 旧后端：绝对时间 ISO 字符串 */
  expiresAtUtc?: string;
  /** 交付后端：有效期（秒） */
  expiresIn?: number;
  user?: RawUserInfo;
}

export interface RawProduct {
  id?: number;
  productCode?: string;
  name?: string;
  description?: string | null;
  categoryId?: number;
  categoryName?: string | null;
  unitPrice?: number | string;
  currentStock?: number;
  minimumStockLevel?: number;
  stockStatus?: string;
  stockValue?: number | string;
  createdAt?: string;
}

export interface RawStockTransaction {
  id?: number;
  transactionReference?: string;
  requestId?: string;
  productId?: number;
  productCode?: string | null;
  productName?: string | null;
  type?: string;
  quantity?: number;
  previousStock?: number;
  newStock?: number;
  notes?: string | null;
  createdAt?: string;
}

export interface RawDashboardMetrics {
  totalProducts?: number;
  totalStockQuantity?: number;
  lowStockProducts?: number;
  outOfStockProducts?: number;
}

export interface RawDashboard {
  /** 交付后端：嵌套在 metrics 下 */
  metrics?: RawDashboardMetrics;
  /** 旧后端：直接平铺在顶层 */
  totalProducts?: number;
  totalStockQuantity?: number;
  lowStockProducts?: number;
  outOfStockProducts?: number;
  recentTransactions?: RawStockTransaction[];
  /** 旧后端 */
  stockChangeLast7Days?: { date?: string; netChange?: number }[];
  /** 交付后端 */
  dailyNetChanges?: { date?: string; netChange?: number }[];
}

export interface RawCategory {
  id?: number;
  name?: string;
  description?: string | null;
  productCount?: number;
}

export interface RawPagedResult<T> {
  items?: T[];
  total?: number;
  page?: number;
  pageSize?: number;
}

/* ------------------------------------------------------------------ */
/* 基础工具                                                            */
/* ------------------------------------------------------------------ */

const DEFAULT_TOKEN_SECONDS = 8 * 60 * 60;

function toNumber(value: unknown, fallback = 0): number {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : fallback;
  }
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }
  return fallback;
}

/** 后端 DateTime 可能带 Z、带偏移，也可能不带时区；统一成字符串交给 DatePipe。 */
function toIsoString(value: unknown): string {
  if (typeof value === 'string') {
    return value;
  }
  if (value instanceof Date && !Number.isNaN(value.getTime())) {
    return value.toISOString();
  }
  if (typeof value === 'number') {
    const fromNumber = new Date(value);
    return Number.isNaN(fromNumber.getTime()) ? '' : fromNumber.toISOString();
  }
  return '';
}

const STOCK_STATUSES: readonly StockStatusValue[] = ['normal', 'low', 'out'];

function toStockStatus(value: unknown, currentStock: number, minimumStockLevel: number): StockStatusValue {
  if (typeof value === 'string' && (STOCK_STATUSES as readonly string[]).includes(value)) {
    return value as StockStatusValue;
  }
  // 后端没给（或给了不认识的值）时按契约口径自己算：0 缺货，≤ 阈值低库存，其余正常
  if (currentStock === 0) {
    return 'out';
  }
  return currentStock <= minimumStockLevel ? 'low' : 'normal';
}

const TRANSACTION_TYPES: readonly TransactionType[] = ['IN', 'OUT', 'ADJUST'];

function toTransactionType(value: unknown): TransactionType {
  const upper = typeof value === 'string' ? value.toUpperCase() : '';
  return (TRANSACTION_TYPES as readonly string[]).includes(upper)
    ? (upper as TransactionType)
    : 'ADJUST';
}

/* ------------------------------------------------------------------ */
/* 归一化函数                                                          */
/* ------------------------------------------------------------------ */

export function normalizeUserInfo(raw: RawUserInfo | null | undefined): UserInfo {
  const source = raw ?? {};
  const roles = Array.isArray(source.roles)
    ? source.roles.filter((role): role is string => typeof role === 'string' && role.length > 0)
    : typeof source.role === 'string' && source.role.length > 0
      ? [source.role]
      : [];

  return {
    id: source.id === undefined || source.id === null ? '' : String(source.id),
    userName: source.userName ?? source.username ?? '',
    roles,
  };
}

/**
 * 登录响应归一化。
 *
 * 交付后端返回 expiresIn（秒）而没有 expiresAtUtc，但 AuthService.isAuthenticated
 * 依赖绝对时间判断会话是否过期，所以这里换算成本地绝对时间。
 */
export function normalizeLogin(raw: RawLoginResponse, now: number = Date.now()): LoginResponse {
  const absolute = typeof raw?.expiresAtUtc === 'string' ? raw.expiresAtUtc.trim() : '';
  const expiresAtUtc =
    absolute !== ''
      ? absolute
      : new Date(now + toNumber(raw?.expiresIn, DEFAULT_TOKEN_SECONDS) * 1000).toISOString();

  return {
    accessToken: raw?.accessToken ?? '',
    expiresAtUtc,
    user: normalizeUserInfo(raw?.user),
  };
}

/** 商品：productCode 接口不返回库存金额，按 unitPrice × currentStock 补齐。 */
export function normalizeProduct(raw: RawProduct | null | undefined): Product {
  const source = raw ?? {};
  const unitPrice = toNumber(source.unitPrice);
  const currentStock = toNumber(source.currentStock);
  const minimumStockLevel = toNumber(source.minimumStockLevel);

  return {
    id: toNumber(source.id),
    productCode: source.productCode ?? '',
    name: source.name ?? '',
    description: source.description ?? null,
    categoryId: toNumber(source.categoryId),
    categoryName: source.categoryName ?? '',
    unitPrice,
    currentStock,
    minimumStockLevel,
    stockStatus: toStockStatus(source.stockStatus, currentStock, minimumStockLevel),
    stockValue: source.stockValue === undefined ? unitPrice * currentStock : toNumber(source.stockValue),
    createdAt: toIsoString(source.createdAt),
  };
}

export function normalizeTransaction(raw: RawStockTransaction | null | undefined): StockTransaction {
  const source = raw ?? {};
  return {
    id: toNumber(source.id),
    transactionReference: source.transactionReference ?? '',
    requestId: source.requestId === undefined || source.requestId === null ? '' : String(source.requestId),
    productId: toNumber(source.productId),
    // 新建流水的响应里没有商品编码/名称（列表接口才有），留空避免页面出现 undefined
    productCode: source.productCode ?? '',
    productName: source.productName ?? '',
    type: toTransactionType(source.type),
    quantity: toNumber(source.quantity),
    previousStock: toNumber(source.previousStock),
    newStock: toNumber(source.newStock),
    notes: source.notes ?? null,
    createdAt: toIsoString(source.createdAt),
  };
}

/** 仪表盘：交付后端把 4 项指标放在 metrics 里，趋势字段叫 dailyNetChanges。 */
export function normalizeDashboard(raw: RawDashboard | null | undefined): Dashboard {
  const source = raw ?? {};
  const metrics: RawDashboardMetrics = source.metrics ?? source;
  const trend = source.stockChangeLast7Days ?? source.dailyNetChanges ?? [];

  const days: DailyNetChange[] = trend.map((day) => ({
    date: day?.date ?? '',
    netChange: toNumber(day?.netChange),
  }));

  return {
    totalProducts: toNumber(metrics.totalProducts),
    totalStockQuantity: toNumber(metrics.totalStockQuantity),
    lowStockProducts: toNumber(metrics.lowStockProducts),
    outOfStockProducts: toNumber(metrics.outOfStockProducts),
    recentTransactions: (source.recentTransactions ?? []).map(normalizeTransaction),
    stockChangeLast7Days: days,
  };
}

export function normalizeCategory(raw: RawCategory | null | undefined): Category {
  const source = raw ?? {};
  return {
    id: toNumber(source.id),
    name: source.name ?? '',
    description: source.description ?? null,
    productCount: toNumber(source.productCount),
  };
}

export function normalizePage<T, R>(
  raw: RawPagedResult<T> | null | undefined,
  mapItem: (item: T) => R,
): PagedResult<R> {
  const source = raw ?? {};
  const items = Array.isArray(source.items) ? source.items : [];
  return {
    items: items.map(mapItem),
    total: toNumber(source.total),
    page: toNumber(source.page, 1),
    pageSize: toNumber(source.pageSize, items.length),
  };
}
