import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, describeFailure } from '../../core/api.service';
import { Category, Product } from '../../core/api.models';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';

type EditorMode = 'create' | 'edit';

@Component({
  selector: 'app-products',
  imports: [ReactiveFormsModule, CurrencyPipe, DatePipe],
  template: `
    <div class="page-head">
      <div>
        <h1>Products</h1>
        <p>Search, filter and maintain the product catalogue. Stock is only changed through stock operations.</p>
      </div>
      <div class="page-actions">
        @if (auth.isAdmin()) {
          <button class="btn" type="button" (click)="openCreate()">New product</button>
        }
      </div>
    </div>

    <section class="card">
      <div class="card-head">
        <form class="filters" [formGroup]="filters" (ngSubmit)="applyFilters()">
          <div class="field" style="min-width: 220px">
            <label for="search">Search</label>
            <input id="search" type="search" formControlName="search" placeholder="Code or name" />
          </div>

          <div class="field" style="min-width: 160px">
            <label for="categoryId">Category</label>
            <select id="categoryId" formControlName="categoryId">
              <option value="">All categories</option>
              @for (category of categories(); track category.id) {
                <option [value]="category.id">{{ category.name }}</option>
              }
            </select>
          </div>

          <div class="field" style="min-width: 150px">
            <label for="stockStatus">Stock status</label>
            <select id="stockStatus" formControlName="stockStatus">
              <option value="all">All</option>
              <option value="normal">Normal</option>
              <option value="low">Low</option>
              <option value="out">Out of stock</option>
            </select>
          </div>

          <button class="btn btn-ghost" type="submit">Apply</button>
          <button class="btn btn-link" type="button" (click)="resetFilters()">Reset</button>
        </form>

        <span class="muted">{{ total() }} product(s)</span>
      </div>

      @if (errorMessage()) {
        <div class="card-pad">
          <div class="alert alert-error" role="alert">{{ errorMessage() }}</div>
        </div>
      }

      @if (loading()) {
        <div class="card-pad">
          @for (row of [1, 2, 3, 4, 5]; track row) {
            <div class="skeleton" style="margin-bottom: 10px"></div>
          }
        </div>
      } @else if (products().length === 0) {
        <div class="state">
          <strong>No products match the filters</strong>
          <p>Try a different keyword or clear the filters.</p>
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
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (product of products(); track product.id) {
                <tr>
                  <td class="code">{{ product.productCode }}</td>
                  <td>
                    <div>{{ product.name }}</div>
                    @if (product.description) {
                      <div class="muted" style="font-size: 12px">{{ product.description }}</div>
                    }
                  </td>
                  <td>{{ product.categoryName }}</td>
                  <td class="num">{{ product.unitPrice | currency: 'USD' : 'symbol' : '1.2-2' }}</td>
                  <td class="num"><strong>{{ product.currentStock }}</strong></td>
                  <td class="num muted">{{ product.minimumStockLevel }}</td>
                  <td>
                    <span class="badge" [class]="'badge-' + product.stockStatus">
                      {{ statusLabel(product.stockStatus) }}
                    </span>
                  </td>
                  <td>
                    <button class="btn btn-link btn-sm" type="button" (click)="openDetail(product)">View</button>
                    @if (auth.isAdmin()) {
                      <button class="btn btn-link btn-sm" type="button" (click)="openEdit(product)">Edit</button>
                      <button class="btn btn-link btn-sm" type="button" (click)="askDelete(product)">Delete</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <div class="pager">
          <span class="pager-info">
            Page {{ page() }} of {{ totalPages() }} · {{ total() }} total
          </span>
          <div class="pager-controls">
            <select
              aria-label="Page size"
              [value]="pageSize()"
              (change)="changePageSize($any($event.target).value)"
            >
              <option value="10">10 / page</option>
              <option value="20">20 / page</option>
              <option value="50">50 / page</option>
            </select>
            <button class="btn btn-ghost btn-sm" type="button" [disabled]="page() <= 1" (click)="goTo(page() - 1)">
              Previous
            </button>
            <button
              class="btn btn-ghost btn-sm"
              type="button"
              [disabled]="page() >= totalPages()"
              (click)="goTo(page() + 1)"
            >
              Next
            </button>
          </div>
        </div>
      }
    </section>

    <!-- 新建 / 编辑 -->
    @if (editorMode()) {
      <div class="modal-backdrop" (click)="closeEditor()">
        <div class="modal" role="dialog" aria-modal="true" [attr.aria-label]="editorMode() === 'create' ? 'New product' : 'Edit product'" (click)="$event.stopPropagation()">
          <div class="modal-head">
            <h2>{{ editorMode() === 'create' ? 'New product' : 'Edit product' }}</h2>
            <button class="icon-btn" type="button" aria-label="Close" (click)="closeEditor()">×</button>
          </div>

          <form [formGroup]="editor" (ngSubmit)="save()" novalidate>
            <div class="modal-body">
              <div class="form-grid">
                <div class="field">
                  <label for="productCode">Product code</label>
                  <input id="productCode" type="text" formControlName="productCode" [readOnly]="editorMode() === 'edit'" />
                  @if (editorMode() === 'edit') {
                    <span class="field-hint">Product code cannot be changed.</span>
                  }
                  @if (editor.controls.productCode.touched && editor.controls.productCode.invalid) {
                    <span class="field-error">Required, max 40 characters.</span>
                  }
                </div>

                <div class="field">
                  <label for="productName">Name</label>
                  <input id="productName" type="text" formControlName="name" />
                  @if (editor.controls.name.touched && editor.controls.name.invalid) {
                    <span class="field-error">Required, max 200 characters.</span>
                  }
                </div>

                <div class="field">
                  <label for="productCategory">Category</label>
                  <select id="productCategory" formControlName="categoryId">
                    <option value="">Select a category</option>
                    @for (category of categories(); track category.id) {
                      <option [value]="category.id">{{ category.name }}</option>
                    }
                  </select>
                  @if (editor.controls.categoryId.touched && editor.controls.categoryId.invalid) {
                    <span class="field-error">Category is required.</span>
                  }
                </div>

                <div class="field">
                  <label for="unitPrice">Unit price (USD)</label>
                  <input id="unitPrice" type="number" min="0" step="0.01" formControlName="unitPrice" />
                  @if (editor.controls.unitPrice.touched && editor.controls.unitPrice.invalid) {
                    <span class="field-error">Must be 0 or greater.</span>
                  }
                </div>

                <div class="field">
                  <label for="minimumStockLevel">Minimum stock level</label>
                  <input id="minimumStockLevel" type="number" min="0" step="1" formControlName="minimumStockLevel" />
                  @if (editor.controls.minimumStockLevel.touched && editor.controls.minimumStockLevel.invalid) {
                    <span class="field-error">Must be 0 or greater.</span>
                  }
                </div>

                <div class="field">
                  <label for="currentStock">Initial stock</label>
                  <input
                    id="currentStock"
                    type="number"
                    min="0"
                    step="1"
                    formControlName="currentStock"
                    [readOnly]="editorMode() === 'edit'"
                  />
                  <span class="field-hint">
                    {{
                      editorMode() === 'create'
                        ? 'Opening balance only — it does not create a stock transaction.'
                        : 'Use Stock operations (IN / OUT / ADJUST) to change stock.'
                    }}
                  </span>
                </div>

                <div class="field span-2">
                  <label for="description">Description</label>
                  <textarea id="description" formControlName="description" rows="2"></textarea>
                </div>
              </div>

              @if (editorError()) {
                <div class="alert alert-error" role="alert" style="margin-top: 14px">{{ editorError() }}</div>
              }
            </div>

            <div class="modal-foot">
              <button class="btn btn-ghost" type="button" (click)="closeEditor()">Cancel</button>
              <button class="btn" type="submit" [disabled]="saving()">
                {{ saving() ? 'Saving…' : 'Save' }}
              </button>
            </div>
          </form>
        </div>
      </div>
    }

    <!-- 详情 -->
    @if (detail(); as product) {
      <div class="modal-backdrop" (click)="detail.set(null)">
        <div class="modal" role="dialog" aria-modal="true" aria-label="Product detail" (click)="$event.stopPropagation()">
          <div class="modal-head">
            <h2>{{ product.name }}</h2>
            <button class="icon-btn" type="button" aria-label="Close" (click)="detail.set(null)">×</button>
          </div>
          <div class="modal-body">
            <dl class="detail-list">
              <dt>Product code</dt>
              <dd class="code">{{ product.productCode }}</dd>
              <dt>Category</dt>
              <dd>{{ product.categoryName }}</dd>
              <dt>Unit price</dt>
              <dd>{{ product.unitPrice | currency: 'USD' : 'symbol' : '1.2-2' }}</dd>
              <dt>Current stock</dt>
              <dd>
                {{ product.currentStock }}
                <span class="badge" [class]="'badge-' + product.stockStatus">{{ statusLabel(product.stockStatus) }}</span>
              </dd>
              <dt>Minimum stock level</dt>
              <dd>{{ product.minimumStockLevel }}</dd>
              <dt>Stock value</dt>
              <dd>{{ product.stockValue | currency: 'USD' : 'symbol' : '1.2-2' }}</dd>
              <dt>Created (UTC)</dt>
              <dd>{{ product.createdAt | date: 'yyyy-MM-dd HH:mm' }}</dd>
              <dt>Description</dt>
              <dd>{{ product.description || '—' }}</dd>
            </dl>
          </div>
          <div class="modal-foot">
            <button class="btn btn-ghost" type="button" (click)="detail.set(null)">Close</button>
          </div>
        </div>
      </div>
    }

    <!-- 删除确认 -->
    @if (deleteTarget(); as product) {
      <div class="modal-backdrop" (click)="deleteTarget.set(null)">
        <div class="modal" role="dialog" aria-modal="true" aria-label="Delete product" (click)="$event.stopPropagation()">
          <div class="modal-head">
            <h2>Delete product</h2>
            <button class="icon-btn" type="button" aria-label="Close" (click)="deleteTarget.set(null)">×</button>
          </div>
          <div class="modal-body">
            <p>
              Delete <strong>{{ product.name }}</strong> ({{ product.productCode }})? Products with stock or
              transaction history cannot be deleted.
            </p>
            @if (deleteError()) {
              <div class="alert alert-error" role="alert" style="margin-top: 12px">{{ deleteError() }}</div>
            }
          </div>
          <div class="modal-foot">
            <button class="btn btn-ghost" type="button" (click)="deleteTarget.set(null)">Cancel</button>
            <button class="btn btn-danger" type="button" [disabled]="deleting()" (click)="confirmDelete(product)">
              {{ deleting() ? 'Deleting…' : 'Delete' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [
    `
      .detail-list {
        display: grid;
        grid-template-columns: 170px minmax(0, 1fr);
        gap: 10px 16px;
        margin: 0;
      }

      .detail-list dt {
        color: var(--text-muted);
        font-size: 13px;
      }

      .detail-list dd {
        margin: 0;
        display: flex;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
      }

      @media (max-width: 620px) {
        .detail-list {
          grid-template-columns: minmax(0, 1fr);
          gap: 2px;
        }

        .detail-list dd {
          margin-bottom: 10px;
        }
      }
    `,
  ],
})
export class ProductsComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly products = signal<Product[]>([]);
  protected readonly categories = signal<Category[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly detail = signal<Product | null>(null);
  protected readonly deleteTarget = signal<Product | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<string | null>(null);

  protected readonly editorMode = signal<EditorMode | null>(null);
  protected readonly editorError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected editingId: number | null = null;

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));

  protected readonly filters = this.fb.nonNullable.group({
    search: [''],
    categoryId: [''],
    stockStatus: ['all'],
  });

  protected readonly editor = this.fb.nonNullable.group({
    productCode: ['', [Validators.required, Validators.maxLength(40)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    categoryId: ['', [Validators.required]],
    unitPrice: ['', [Validators.required, Validators.min(0)]],
    currentStock: ['0', [Validators.required, Validators.min(0)]],
    minimumStockLevel: ['0', [Validators.required, Validators.min(0)]],
    description: [''],
  });

  ngOnInit(): void {
    this.api.getCategories().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => this.toast.error('Could not load categories.'),
    });

    this.load();
  }

  protected applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  protected resetFilters(): void {
    this.filters.reset({ search: '', categoryId: '', stockStatus: 'all' });
    this.page.set(1);
    this.load();
  }

  protected goTo(page: number): void {
    if (page < 1 || page > this.totalPages()) {
      return;
    }

    this.page.set(page);
    this.load();
  }

  protected changePageSize(value: string): void {
    this.pageSize.set(Number(value));
    this.page.set(1);
    this.load();
  }

  protected openCreate(): void {
    this.editorMode.set('create');
    this.editingId = null;
    this.editorError.set(null);
    this.editor.reset({
      productCode: '',
      name: '',
      categoryId: '',
      unitPrice: '',
      currentStock: '0',
      minimumStockLevel: '0',
      description: '',
    });
    this.editor.controls.currentStock.enable();
  }

  protected openEdit(product: Product): void {
    this.editorMode.set('edit');
    this.editingId = product.id;
    this.editorError.set(null);

    this.editor.reset({
      productCode: product.productCode,
      name: product.name,
      categoryId: String(product.categoryId),
      unitPrice: String(product.unitPrice),
      currentStock: String(product.currentStock),
      minimumStockLevel: String(product.minimumStockLevel),
      description: product.description ?? '',
    });

    this.editor.controls.productCode.disable();
    this.editor.controls.currentStock.disable();
  }

  protected closeEditor(): void {
    if (this.saving()) {
      return;
    }

    this.editorMode.set(null);
    this.editor.controls.productCode.enable();
    this.editor.controls.currentStock.enable();
  }

  protected save(): void {
    if (this.saving()) {
      return;
    }

    if (this.editor.invalid) {
      this.editor.markAllAsTouched();
      return;
    }

    const raw = this.editor.getRawValue();
    this.saving.set(true);
    this.editorError.set(null);

    if (this.editorMode() === 'create') {
      this.api
        .createProduct({
          productCode: raw.productCode.trim(),
          name: raw.name.trim(),
          description: raw.description.trim() || null,
          categoryId: Number(raw.categoryId),
          unitPrice: Number(raw.unitPrice),
          currentStock: Number(raw.currentStock),
          minimumStockLevel: Number(raw.minimumStockLevel),
        })
        .subscribe({
          next: (created) => {
            this.saving.set(false);
            this.editorMode.set(null);
            this.toast.success(`Product ${created.productCode} created.`);
            this.load();
          },
          error: (error: unknown) => {
            this.saving.set(false);
            this.editorError.set(describeFailure(error));
          },
        });

      return;
    }

    if (this.editingId === null) {
      this.saving.set(false);
      return;
    }

    // 编辑只提交允许修改的五个字段，绝不发送 productCode / currentStock / createdAt。
    this.api
      .updateProduct(this.editingId, {
        name: raw.name.trim(),
        description: raw.description.trim() || null,
        categoryId: Number(raw.categoryId),
        unitPrice: Number(raw.unitPrice),
        minimumStockLevel: Number(raw.minimumStockLevel),
      })
      .subscribe({
        next: (updated) => {
          this.saving.set(false);
          this.editorMode.set(null);
          this.toast.success(`Product ${updated.productCode} updated.`);
          this.load();
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.editorError.set(describeFailure(error));
        },
      });
  }

  protected openDetail(product: Product): void {
    this.detail.set(product);
  }

  protected askDelete(product: Product): void {
    this.deleteError.set(null);
    this.deleteTarget.set(product);
  }

  protected confirmDelete(product: Product): void {
    if (this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.deleteError.set(null);

    this.api.deleteProduct(product.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.deleteTarget.set(null);
        this.toast.success(`Product ${product.productCode} deleted.`);
        this.load();
      },
      error: (error: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(describeFailure(error));
      },
    });
  }

  protected statusLabel(status: string): string {
    switch (status) {
      case 'low':
        return 'Low stock';
      case 'out':
        return 'Out of stock';
      default:
        return 'Normal';
    }
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    const filters = this.filters.getRawValue();
    const categoryId = filters.categoryId ? Number(filters.categoryId) : null;

    this.api
      .getProducts({
        search: filters.search.trim() || undefined,
        categoryId,
        stockStatus: filters.stockStatus,
        page: this.page(),
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (result) => {
          this.products.set(result.items);
          this.total.set(result.total);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.errorMessage.set(describeFailure(error));
          this.products.set([]);
          this.loading.set(false);
        },
      });
  }
}
