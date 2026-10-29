import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ApiService, describeFailure } from '../../core/api.service';
import { Category, Product, StockTransaction } from '../../core/api.models';
import { ToastService } from '../../core/toast.service';

type Tab = 'products' | 'transactions';

@Component({
  selector: 'app-reports',
  imports: [ReactiveFormsModule, CurrencyPipe, DatePipe],
  template: `
    <div class="page-head">
      <div>
        <h1>Reports</h1>
        <p>Filtering and paging are executed on the server — the browser never loads the full data set.</p>
      </div>
    </div>

    <div class="tabs" role="tablist">
      <button type="button" role="tab" [class.active]="tab() === 'products'" (click)="switchTab('products')">
        Product stock
      </button>
      <button type="button" role="tab" [class.active]="tab() === 'transactions'" (click)="switchTab('transactions')">
        Transaction history
      </button>
    </div>

    @if (tab() === 'products') {
      <section class="card">
        <div class="card-head">
          <form class="filters" [formGroup]="productFilters" (ngSubmit)="applyProductFilters()">
            <div class="field" style="min-width: 200px">
              <label for="reportSearch">Search</label>
              <input id="reportSearch" type="search" formControlName="search" placeholder="Code or name" />
            </div>

            <div class="field" style="min-width: 150px">
              <label for="reportCategory">Category</label>
              <select id="reportCategory" formControlName="categoryId">
                <option value="">All categories</option>
                @for (category of categories(); track category.id) {
                  <option [value]="category.id">{{ category.name }}</option>
                }
              </select>
            </div>

            <div class="field" style="min-width: 140px">
              <label for="reportStatus">Stock status</label>
              <select id="reportStatus" formControlName="stockStatus">
                <option value="all">All</option>
                <option value="low">Low stock only</option>
                <option value="out">Out of stock only</option>
                <option value="normal">Normal only</option>
              </select>
            </div>

            <button class="btn btn-ghost" type="submit">Apply</button>
          </form>

          <div class="summary">
            <span class="muted">{{ productTotal() }} row(s)</span>
            <span class="summary-value">{{ pageStockValue() | currency: 'USD' : 'symbol' : '1.2-2' }}</span>
            <span class="muted">value on this page</span>
          </div>
        </div>

        @if (productError()) {
          <div class="card-pad"><div class="alert alert-error" role="alert">{{ productError() }}</div></div>
        }

        @if (productLoading()) {
          <div class="card-pad">
            @for (row of [1, 2, 3, 4]; track row) {
              <div class="skeleton" style="margin-bottom: 10px"></div>
            }
          </div>
        } @else if (productRows().length === 0) {
          <div class="state">
            <strong>No rows match the filters</strong>
            <p>Adjust the filters and try again.</p>
          </div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Code</th>
                  <th>Name</th>
                  <th>Category</th>
                  <th class="num">Unit price</th>
                  <th class="num">Stock</th>
                  <th class="num">Threshold</th>
                  <th>Status</th>
                  <th class="num">Stock value</th>
                </tr>
              </thead>
              <tbody>
                @for (row of productRows(); track row.id) {
                  <tr>
                    <td class="code">{{ row.productCode }}</td>
                    <td>{{ row.name }}</td>
                    <td>{{ row.categoryName }}</td>
                    <td class="num">{{ row.unitPrice | currency: 'USD' : 'symbol' : '1.2-2' }}</td>
                    <td class="num">{{ row.currentStock }}</td>
                    <td class="num muted">{{ row.minimumStockLevel }}</td>
                    <td><span class="badge" [class]="'badge-' + row.stockStatus">{{ statusLabel(row.stockStatus) }}</span></td>
                    <td class="num">{{ row.stockValue | currency: 'USD' : 'symbol' : '1.2-2' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <div class="pager">
            <span class="pager-info">Page {{ productPage() }} of {{ productPages() }}</span>
            <div class="pager-controls">
              <button class="btn btn-ghost btn-sm" type="button" [disabled]="productPage() <= 1" (click)="goProductPage(productPage() - 1)">
                Previous
              </button>
              <button
                class="btn btn-ghost btn-sm"
                type="button"
                [disabled]="productPage() >= productPages()"
                (click)="goProductPage(productPage() + 1)"
              >
                Next
              </button>
            </div>
          </div>
        }
      </section>
    } @else {
      <section class="card">
        <div class="card-head">
          <form class="filters" [formGroup]="transactionFilters" (ngSubmit)="applyTransactionFilters()">
            <div class="field" style="min-width: 190px">
              <label for="txProduct">Product</label>
              <input id="txProduct" type="search" [formControl]="productSearch" placeholder="All products" autocomplete="off" />
              @if (selectedProduct(); as product) {
                <span class="field-hint">
                  Filtering by {{ product.productCode }}
                  <button type="button" class="btn btn-link btn-sm" (click)="clearProductFilter()">clear</button>
                </span>
              }
            </div>

            <div class="field" style="min-width: 130px">
              <label for="txType">Type</label>
              <select id="txType" formControlName="type">
                <option value="">All types</option>
                <option value="IN">IN</option>
                <option value="OUT">OUT</option>
                <option value="ADJUST">ADJUST</option>
              </select>
            </div>

            <div class="field">
              <label for="txFrom">From (UTC)</label>
              <input id="txFrom" type="datetime-local" formControlName="fromUtc" />
            </div>

            <div class="field">
              <label for="txTo">To (UTC, exclusive)</label>
              <input id="txTo" type="datetime-local" formControlName="toUtc" />
            </div>

            <button class="btn btn-ghost" type="submit">Apply</button>
          </form>

          <span class="muted">{{ transactionTotal() }} row(s)</span>
        </div>

        @if (productMatches().length > 0 && !selectedProduct()) {
          <ul class="picker">
            @for (product of productMatches(); track product.id) {
              <li>
                <button type="button" class="picker-item" (click)="selectProduct(product)">
                  <span class="code">{{ product.productCode }}</span>
                  <span>{{ product.name }}</span>
                </button>
              </li>
            }
          </ul>
        }

        @if (transactionError()) {
          <div class="card-pad"><div class="alert alert-error" role="alert">{{ transactionError() }}</div></div>
        }

        @if (transactionLoading()) {
          <div class="card-pad">
            @for (row of [1, 2, 3, 4]; track row) {
              <div class="skeleton" style="margin-bottom: 10px"></div>
            }
          </div>
        } @else if (transactionRows().length === 0) {
          <div class="state">
            <strong>No transactions in this range</strong>
            <p>Widen the time range or clear the filters.</p>
          </div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Reference</th>
                  <th>Product</th>
                  <th>Type</th>
                  <th class="num">Qty</th>
                  <th class="num">Before</th>
                  <th class="num">After</th>
                  <th>Notes</th>
                  <th>When (UTC)</th>
                </tr>
              </thead>
              <tbody>
                @for (row of transactionRows(); track row.id) {
                  <tr>
                    <td class="code">{{ row.transactionReference }}</td>
                    <td>
                      <div>{{ row.productName }}</div>
                      <div class="muted code">{{ row.productCode }}</div>
                    </td>
                    <td><span class="badge" [class]="badgeClass(row.type)">{{ row.type }}</span></td>
                    <td class="num">{{ row.quantity }}</td>
                    <td class="num muted">{{ row.previousStock }}</td>
                    <td class="num"><strong>{{ row.newStock }}</strong></td>
                    <td class="muted">{{ row.notes || '—' }}</td>
                    <td class="muted">{{ row.createdAt | date: 'yyyy-MM-dd HH:mm:ss' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <div class="pager">
            <span class="pager-info">Page {{ transactionPage() }} of {{ transactionPages() }}</span>
            <div class="pager-controls">
              <button
                class="btn btn-ghost btn-sm"
                type="button"
                [disabled]="transactionPage() <= 1"
                (click)="goTransactionPage(transactionPage() - 1)"
              >
                Previous
              </button>
              <button
                class="btn btn-ghost btn-sm"
                type="button"
                [disabled]="transactionPage() >= transactionPages()"
                (click)="goTransactionPage(transactionPage() + 1)"
              >
                Next
              </button>
            </div>
          </div>
        }
      </section>
    }
  `,
  styles: [
    `
      .summary {
        display: flex;
        align-items: baseline;
        gap: 8px;
        flex-wrap: wrap;
      }

      .summary-value {
        font-size: 18px;
        font-weight: 650;
      }

      .picker {
        list-style: none;
        margin: 0;
        padding: 6px;
        border-bottom: 1px solid var(--border);
        background: var(--surface-muted);
      }

      .picker-item {
        display: grid;
        grid-template-columns: 90px minmax(0, 1fr);
        gap: 10px;
        width: 100%;
        text-align: left;
        background: none;
        border: none;
        padding: 7px 10px;
        border-radius: 6px;
        font: inherit;
        cursor: pointer;
      }

      .picker-item:hover {
        background: var(--surface);
      }
    `,
  ],
})
export class ReportsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(ToastService);

  protected readonly tab = signal<Tab>('products');
  protected readonly categories = signal<Category[]>([]);

  protected readonly productRows = signal<Product[]>([]);
  protected readonly productTotal = signal(0);
  protected readonly productPage = signal(1);
  protected readonly productPageSize = 20;
  protected readonly productLoading = signal(false);
  protected readonly productError = signal<string | null>(null);

  protected readonly transactionRows = signal<StockTransaction[]>([]);
  protected readonly transactionTotal = signal(0);
  protected readonly transactionPage = signal(1);
  protected readonly transactionPageSize = 20;
  protected readonly transactionLoading = signal(false);
  protected readonly transactionError = signal<string | null>(null);

  protected readonly productSearch = this.fb.nonNullable.control('');
  protected readonly productMatches = signal<Product[]>([]);
  protected readonly selectedProduct = signal<Product | null>(null);

  protected readonly productPages = computed(() =>
    Math.max(1, Math.ceil(this.productTotal() / this.productPageSize)),
  );

  protected readonly transactionPages = computed(() =>
    Math.max(1, Math.ceil(this.transactionTotal() / this.transactionPageSize)),
  );

  protected readonly pageStockValue = computed(() =>
    this.productRows().reduce((sum, row) => sum + row.stockValue, 0),
  );

  protected readonly productFilters = this.fb.nonNullable.group({
    search: [''],
    categoryId: [''],
    stockStatus: ['all'],
  });

  protected readonly transactionFilters = this.fb.nonNullable.group({
    type: [''],
    fromUtc: [''],
    toUtc: [''],
  });

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.api.getCategories().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => this.toast.error('Could not load categories.'),
    });

    this.loadProducts();

    this.productSearch.valueChanges.subscribe(() => {
      if (this.searchTimer) {
        clearTimeout(this.searchTimer);
      }

      const term = this.productSearch.value.trim();
      if (term.length === 0) {
        this.productMatches.set([]);
        return;
      }

      this.searchTimer = setTimeout(() => {
        this.api.getProducts({ search: term, pageSize: 8 }).subscribe({
          next: (result) => this.productMatches.set(result.items),
          error: () => this.productMatches.set([]),
        });
      }, 250);
    });
  }

  protected switchTab(tab: Tab): void {
    this.tab.set(tab);

    if (tab === 'products' && this.productRows().length === 0) {
      this.loadProducts();
    }

    if (tab === 'transactions' && this.transactionRows().length === 0) {
      this.loadTransactions();
    }
  }

  protected applyProductFilters(): void {
    this.productPage.set(1);
    this.loadProducts();
  }

  protected goProductPage(page: number): void {
    if (page < 1 || page > this.productPages()) {
      return;
    }

    this.productPage.set(page);
    this.loadProducts();
  }

  protected applyTransactionFilters(): void {
    this.transactionPage.set(1);
    this.loadTransactions();
  }

  protected goTransactionPage(page: number): void {
    if (page < 1 || page > this.transactionPages()) {
      return;
    }

    this.transactionPage.set(page);
    this.loadTransactions();
  }

  protected selectProduct(product: Product): void {
    this.selectedProduct.set(product);
    this.productMatches.set([]);
    this.productSearch.setValue(product.productCode, { emitEvent: false });
    this.transactionPage.set(1);
    this.loadTransactions();
  }

  protected clearProductFilter(): void {
    this.selectedProduct.set(null);
    this.productSearch.setValue('');
    this.transactionPage.set(1);
    this.loadTransactions();
  }

  protected statusLabel(status: string): string {
    return status === 'low' ? 'Low stock' : status === 'out' ? 'Out of stock' : 'Normal';
  }

  protected badgeClass(type: string): string {
    return type === 'IN' ? 'badge-in' : type === 'OUT' ? 'badge-out' : 'badge-adjust';
  }

  private loadProducts(): void {
    this.productLoading.set(true);
    this.productError.set(null);

    const filters = this.productFilters.getRawValue();

    this.api
      .getProductReport({
        search: filters.search.trim() || undefined,
        categoryId: filters.categoryId ? Number(filters.categoryId) : null,
        stockStatus: filters.stockStatus,
        page: this.productPage(),
        pageSize: this.productPageSize,
      })
      .subscribe({
        next: (result) => {
          this.productRows.set(result.items);
          this.productTotal.set(result.total);
          this.productLoading.set(false);
        },
        error: (error: unknown) => {
          this.productError.set(describeFailure(error));
          this.productRows.set([]);
          this.productLoading.set(false);
        },
      });
  }

  private loadTransactions(): void {
    this.transactionLoading.set(true);
    this.transactionError.set(null);

    const filters = this.transactionFilters.getRawValue();

    this.api
      .getTransactionReport({
        productId: this.selectedProduct()?.id ?? null,
        type: filters.type || undefined,
        fromUtc: toUtcIso(filters.fromUtc),
        toUtc: toUtcIso(filters.toUtc),
        page: this.transactionPage(),
        pageSize: this.transactionPageSize,
      })
      .subscribe({
        next: (result) => {
          this.transactionRows.set(result.items);
          this.transactionTotal.set(result.total);
          this.transactionLoading.set(false);
        },
        error: (error: unknown) => {
          this.transactionError.set(describeFailure(error));
          this.transactionRows.set([]);
          this.transactionLoading.set(false);
        },
      });
  }
}

/** datetime-local 的值按用户本地时间理解，统一换成 UTC ISO 再发给后端。 */
function toUtcIso(value: string): string | undefined {
  if (!value) {
    return undefined;
  }

  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}
