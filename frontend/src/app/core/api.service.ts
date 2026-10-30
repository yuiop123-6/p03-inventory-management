import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable, catchError, map, throwError } from 'rxjs';
import {
  ApiErrorBody,
  Category,
  CreateProductPayload,
  Dashboard,
  LoginResponse,
  PagedResult,
  Product,
  ProductQuery,
  StockCommandPayload,
  StockTransaction,
  TransactionQuery,
  UpdateProductPayload,
  UserInfo,
} from './api.models';
import {
  RawCategory,
  RawDashboard,
  RawLoginResponse,
  RawPagedResult,
  RawProduct,
  RawStockTransaction,
  RawUserInfo,
  normalizeCategory,
  normalizeDashboard,
  normalizeLogin,
  normalizePage,
  normalizeProduct,
  normalizeTransaction,
  normalizeUserInfo,
} from './api.normalize';
import { environment } from '../../environments/environment';

/**
 * API 入口地址来自 src/environments：
 * 开发环境为同源 /api（dev-server 代理转发到 http://localhost:5000），
 * 生产环境由 environment.production.ts 的 apiUrl 指定后端地址。
 */
const BASE = environment.apiUrl;

export class ApiFailure extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly details?: unknown,
    readonly traceId?: string,
  ) {
    super(message);
  }
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  login(userName: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<RawLoginResponse>(`${BASE}/auth/login`, { userName, password })
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizeLogin(raw)),
      );
  }

  me(): Observable<UserInfo> {
    return this.http
      .get<RawUserInfo>(`${BASE}/auth/me`)
      .pipe(map((raw) => normalizeUserInfo(raw)));
  }

  getProducts(query: ProductQuery): Observable<PagedResult<Product>> {
    return this.http
      .get<RawPagedResult<RawProduct>>(`${BASE}/products`, { params: this.toParams(query) })
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizePage(raw, normalizeProduct)),
      );
  }

  getProduct(id: number): Observable<Product> {
    return this.http
      .get<RawProduct>(`${BASE}/products/${id}`)
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizeProduct(raw)),
      );
  }

  createProduct(payload: CreateProductPayload): Observable<Product> {
    return this.http
      .post<RawProduct>(`${BASE}/products`, payload)
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizeProduct(raw)),
      );
  }

  updateProduct(id: number, payload: UpdateProductPayload): Observable<Product> {
    return this.http
      .put<RawProduct>(`${BASE}/products/${id}`, payload)
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizeProduct(raw)),
      );
  }

  deleteProduct(id: number): Observable<void> {
    return this.http
      .delete<void>(`${BASE}/products/${id}`)
      .pipe(catchError((error) => this.translate(error)));
  }

  getCategories(): Observable<Category[]> {
    // 契约（成员 A/B 后端）：GET /categories 返回分页结构 {items,...}，取 items；
    // 兼容返回裸数组的旧实现。
    return this.http
      .get<RawCategory[] | RawPagedResult<RawCategory>>(`${BASE}/categories`)
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => {
          const items = Array.isArray(raw) ? raw : Array.isArray(raw?.items) ? raw.items : [];
          return items.map(normalizeCategory);
        }),
      );
  }

  createCategory(payload: { name: string; description?: string | null }): Observable<Category> {
    return this.http
      .post<RawCategory>(`${BASE}/categories`, payload)
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizeCategory(raw)),
      );
  }

  updateCategory(
    id: number,
    payload: { name: string; description?: string | null },
  ): Observable<Category> {
    return this.http
      .put<RawCategory>(`${BASE}/categories/${id}`, payload)
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizeCategory(raw)),
      );
  }

  deleteCategory(id: number): Observable<void> {
    return this.http
      .delete<void>(`${BASE}/categories/${id}`)
      .pipe(catchError((error) => this.translate(error)));
  }

  createStockTransaction(payload: StockCommandPayload): Observable<StockTransaction> {
    return this.http
      .post<RawStockTransaction>(`${BASE}/stock-transactions`, payload)
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizeTransaction(raw)),
      );
  }

  getStockTransactions(query: TransactionQuery): Observable<PagedResult<StockTransaction>> {
    return this.http
      .get<RawPagedResult<RawStockTransaction>>(`${BASE}/stock-transactions`, {
        params: this.toParams(query),
      })
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizePage(raw, normalizeTransaction)),
      );
  }

  getDashboard(): Observable<Dashboard> {
    return this.http
      .get<RawDashboard>(`${BASE}/dashboard`)
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizeDashboard(raw)),
      );
  }

  getProductReport(query: ProductQuery): Observable<PagedResult<Product>> {
    return this.http
      .get<RawPagedResult<RawProduct>>(`${BASE}/reports/products`, {
        params: this.toParams(query),
      })
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizePage(raw, normalizeProduct)),
      );
  }

  getTransactionReport(query: TransactionQuery): Observable<PagedResult<StockTransaction>> {
    return this.http
      .get<RawPagedResult<RawStockTransaction>>(`${BASE}/reports/transactions`, {
        params: this.toParams(query),
      })
      .pipe(
        catchError((error) => this.translate(error)),
        map((raw) => normalizePage(raw, normalizeTransaction)),
      );
  }

  private toParams(source: object): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(source)) {
      if (value === null || value === undefined || value === '') {
        continue;
      }
      params = params.set(key, String(value));
    }
    return params;
  }

  private translate(error: unknown) {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as ApiErrorBody | null;

      if (body && typeof body === 'object' && 'code' in body) {
        return throwError(
          () => new ApiFailure(error.status, body.code, body.message, body.details, body.traceId),
        );
      }

      if (error.status === 0) {
        return throwError(
          () => new ApiFailure(0, 'NETWORK_ERROR', '无法连接服务器，请确认后端已启动。'),
        );
      }

      return throwError(
        () => new ApiFailure(error.status, 'HTTP_ERROR', `请求失败（${error.status}）。`),
      );
    }

    return throwError(() => error);
  }
}

export function describeFailure(error: unknown): string {
  if (error instanceof ApiFailure) {
    switch (error.code) {
      case 'INSUFFICIENT_STOCK':
        return error.message;
      case 'STOCK_CONFLICT':
        return '库存刚被其他人改过，请刷新后重试。';
      case 'IDEMPOTENCY_CONFLICT':
        return '该请求编号已用于另一笔库存操作，请刷新页面重新提交。';
      case 'REFERENTIAL_CONFLICT':
        return error.message;
      case 'DUPLICATE_VALUE':
        return error.message;
      case 'VALIDATION_ERROR':
        return error.message;
      case 'FORBIDDEN':
        return '当前角色没有执行该操作的权限。';
      case 'UNAUTHORIZED':
        return '登录已过期，请重新登录。';
      default:
        return error.message;
    }
  }

  return '发生未知错误，请稍后重试。';
}
