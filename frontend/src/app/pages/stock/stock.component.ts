import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService, describeFailure } from '../../core/api.service';
import { Product, StockTransaction, TransactionType } from '../../core/api.models';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';

@Component({
  selector: 'app-stock',
  imports: [ReactiveFormsModule, DatePipe],
  template: `
    <div class="page-head">
      <div>
        <h1>Stock operations</h1>
        <p>Every successful movement updates stock and writes one transaction in the same database transaction.</p>
      </div>
    </div>

    <div class="split">
      <section class="card">
        <div class="card-head">
          <h2>Record a movement</h2>
        </div>

        <form class="card-pad" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="productSearch">Find a product</label>
            <input
              id="productSearch"
              type="search"
              [formControl]="searchControl"
              placeholder="Type a product code or name"
              autocomplete="off"
            />
            @if (searching()) {
              <span class="field-hint">Searching…</span>
            }
          </div>

          @if (results().length > 0 && !selected()) {
            <ul class="picker" role="listbox" aria-label="Search results">
              @for (product of results(); track product.id) {
                <li>
                  <button type="button" class="picker-item" (click)="select(product)">
                    <span class="code">{{ product.productCode }}</span>
                    <span>{{ product.name }}</span>
                    <span class="muted">stock {{ product.currentStock }}</span>
                  </button>
                </li>
              }
            </ul>
          }

          @if (selected(); as product) {
            <div class="selected-card">
              <div>
                <div class="code">{{ product.productCode }}</div>
                <strong>{{ product.name }}</strong>
              </div>
              <div class="selected-stats">
                <div>
                  <span class="muted">Current</span>
                  <strong>{{ product.currentStock }}</strong>
                </div>
                <div>
                  <span class="muted">Threshold</span>
                  <strong>{{ product.minimumStockLevel }}</strong>
                </div>
                <button type="button" class="btn btn-link btn-sm" (click)="clearSelection()">Change</button>
              </div>
            </div>
          }

          <div class="form-grid" style="margin-top: 16px">
            <div class="field">
              <label for="type">Type</label>
              <select id="type" formControlName="type">
                <option value="IN">IN — receive stock</option>
                <option value="OUT">OUT — issue stock</option>
                <option value="ADJUST" [disabled]="!auth.isAdmin()">ADJUST — set exact stock</option>
              </select>
              @if (!auth.isAdmin()) {
                <span class="field-hint">Operators cannot run ADJUST.</span>
              }
            </div>

            <div class="field">
              <label for="quantity">{{ form.controls.type.value === 'ADJUST' ? 'New stock level' : 'Quantity' }}</label>
              <input id="quantity" type="number" min="0" step="1" formControlName="quantity" />
              @if (form.controls.quantity.touched && form.controls.quantity.invalid) {
                <span class="field-error">Enter a valid whole number.</span>
              }
            </div>

            <div class="field span-2">
              <label for="notes">Notes (optional)</label>
              <input id="notes" type="text" formControlName="notes" maxlength="1000" placeholder="Reason or reference" />
            </div>
          </div>

          @if (previewMessage(); as preview) {
            <div class="alert" [class.alert-error]="preview.blocked" [class.alert-info]="!preview.blocked">
              {{ preview.text }}
            </div>
          }

          @if (submitError()) {
            <div class="alert alert-error" role="alert">{{ submitError() }}</div>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="submitting() || !selected() || form.invalid">
              {{ submitting() ? 'Submitting…' : 'Submit movement' }}
            </button>
            <span class="field-hint">Request ID: <span class="code">{{ requestId().slice(0, 8) }}…</span></span>
          </div>
        </form>
      </section>

      <section class="card">
        <div class="card-head">
          <h2>Last movements for this product</h2>
        </div>

        @if (!selected()) {
          <div class="state">
            <strong>Select a product</strong>
            <p>Its recent movements will appear here.</p>
          </div>
        } @else if (historyLoading()) {
          <div class="card-pad">
            @for (row of [1, 2, 3]; track row) {
              <div class="skeleton" style="margin-bottom: 10px"></div>
            }
          </div>
        } @else if (history().length === 0) {
          <div class="state">
            <strong>No movements yet</strong>
            <p>This product has no stock transactions.</p>
          </div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Ref</th>
                  <th>Type</th>
                  <th class="num">Qty</th>
                  <th class="num">Balance</th>
                  <th>When (UTC)</th>
                </tr>
              </thead>
              <tbody>
                @for (item of history(); track item.id) {
                  <tr>
                    <td class="code">{{ item.transactionReference }}</td>
                    <td><span class="badge" [class]="badgeClass(item.type)">{{ item.type }}</span></td>
                    <td class="num">{{ item.quantity }}</td>
                    <td class="num">{{ item.previousStock }} → <strong>{{ item.newStock }}</strong></td>
                    <td class="muted">{{ item.createdAt | date: 'MM-dd HH:mm' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        @if (lastResult(); as result) {
          <div class="result">
            <div class="result-head">
              <span class="badge badge-in">Recorded</span>
              <span class="code">{{ result.transactionReference }}</span>
            </div>
            <div class="result-body">
              <span>{{ result.type }} {{ result.quantity }}</span>
              <span class="muted">{{ result.previousStock }} →</span>
              <strong>{{ result.newStock }}</strong>
            </div>
          </div>
        }
      </section>
    </div>
  `,
  styles: [
    `
      .picker {
        list-style: none;
        margin: 8px 0 0;
        padding: 4px;
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        max-height: 220px;
        overflow: auto;
        background: var(--surface);
      }

      .picker-item {
        display: grid;
        grid-template-columns: 80px minmax(0, 1fr) auto;
        gap: 10px;
        align-items: center;
        width: 100%;
        text-align: left;
        background: none;
        border: none;
        padding: 8px 10px;
        border-radius: 6px;
        font: inherit;
        cursor: pointer;
      }

      .picker-item:hover {
        background: var(--surface-muted);
      }

      .selected-card {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 16px;
        flex-wrap: wrap;
        margin-top: 12px;
        padding: 12px 14px;
        border: 1px solid #f6c9ac;
        background: var(--primary-soft);
        border-radius: var(--radius-sm);
      }

      .selected-stats {
        display: flex;
        align-items: center;
        gap: 18px;
      }

      .selected-stats div {
        display: flex;
        flex-direction: column;
        font-size: 12px;
      }

      .selected-stats strong {
        font-size: 16px;
      }

      .actions {
        display: flex;
        align-items: center;
        gap: 14px;
        margin-top: 16px;
        flex-wrap: wrap;
      }

      .result {
        margin: 14px 18px 18px;
        border: 1px solid #bfdcc8;
        background: var(--success-soft);
        border-radius: var(--radius-sm);
        padding: 12px 14px;
      }

      .result-head {
        display: flex;
        align-items: center;
        gap: 10px;
        margin-bottom: 6px;
      }

      .result-body {
        display: flex;
        align-items: baseline;
        gap: 8px;
        font-size: 18px;
      }

      .result-body strong {
        font-size: 22px;
      }

      .alert {
        margin-top: 14px;
      }
    `,
  ],
})
export class StockComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly searchControl = this.fb.nonNullable.control('');
  protected readonly results = signal<Product[]>([]);
  protected readonly searching = signal(false);
  protected readonly selected = signal<Product | null>(null);

  protected readonly history = signal<StockTransaction[]>([]);
  protected readonly historyLoading = signal(false);

  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly lastResult = signal<StockTransaction | null>(null);
  protected readonly requestId = signal<string>(newRequestId());

  protected readonly form = this.fb.nonNullable.group({
    type: ['IN' as TransactionType, [Validators.required]],
    quantity: ['', [Validators.required, Validators.min(0), Validators.pattern(/^\d+$/)]],
    notes: [''],
  });

  /** 提交前的实时预览：会不会库存不足、提交后库存变成多少。 */
  protected readonly previewMessage = computed<{ text: string; blocked: boolean } | null>(() => {
    const product = this.selected();
    const raw = this.form.controls.quantity.value;
    if (!product || raw === null || raw === '') {
      return null;
    }

    const quantity = Number(raw);
    if (!Number.isInteger(quantity) || quantity < 0) {
      return { text: 'Quantity must be a whole number.', blocked: true };
    }

    const type = this.form.controls.type.value;
    if (type === 'IN') {
      return { text: `Stock will go from ${product.currentStock} to ${product.currentStock + quantity}.`, blocked: false };
    }

    if (type === 'OUT') {
      if (quantity === 0) {
        return { text: 'OUT quantity must be at least 1.', blocked: true };
      }

      if (quantity > product.currentStock) {
        return {
          text: `Only ${product.currentStock} in stock — the server will reject this with INSUFFICIENT_STOCK.`,
          blocked: true,
        };
      }

      return { text: `Stock will go from ${product.currentStock} to ${product.currentStock - quantity}.`, blocked: false };
    }

    return { text: `Stock will be set to exactly ${quantity} (currently ${product.currentStock}).`, blocked: false };
  });

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.searchControl.valueChanges.subscribe(() => {
      this.queueSearch();
    });

    this.form.valueChanges.subscribe(() => {
      // 负载变了就换一个新的 RequestId，避免把不同内容当成重放。
      this.requestId.set(newRequestId());
      this.submitError.set(null);
    });
  }

  protected select(product: Product): void {
    this.selected.set(product);
    this.results.set([]);
    this.searchControl.setValue(product.productCode, { emitEvent: false });
    this.requestId.set(newRequestId());
    this.loadHistory(product.id);
  }

  protected clearSelection(): void {
    this.selected.set(null);
    this.history.set([]);
    this.searchControl.setValue('');
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }

    const product = this.selected();
    if (!product) {
      this.toast.error('Select a product first.');
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { type, quantity, notes } = this.form.getRawValue();
    const quantityValue = Number(quantity);

    this.submitting.set(true);
    this.submitError.set(null);

    // 同一个 requestId 一直复用，网络失败后重试也不会重复扣减。
    this.api
      .createStockTransaction({
        requestId: this.requestId(),
        productId: product.id,
        type,
        quantity: quantityValue,
        notes: notes.trim() || null,
      })
      .subscribe({
        next: (transaction) => {
          this.submitting.set(false);
          this.lastResult.set(transaction);
          this.toast.success(`${transaction.type} recorded: ${transaction.previousStock} → ${transaction.newStock}`);
          this.form.patchValue({ quantity: '', notes: '' });
          this.requestId.set(newRequestId());
          this.refreshProduct(product.id);
          this.loadHistory(product.id);
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          this.submitError.set(describeFailure(error));
        },
      });
  }

  protected badgeClass(type: string): string {
    return type === 'IN' ? 'badge-in' : type === 'OUT' ? 'badge-out' : 'badge-adjust';
  }

  private queueSearch(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }

    const term = this.searchControl.value.trim();
    if (term.length === 0) {
      this.results.set([]);
      return;
    }

    this.searchTimer = setTimeout(() => this.runSearch(term), 250);
  }

  private runSearch(term: string): void {
    this.searching.set(true);
    this.api.getProducts({ search: term, pageSize: 10 }).subscribe({
      next: (result) => {
        this.results.set(result.items);
        this.searching.set(false);
      },
      error: () => {
        this.results.set([]);
        this.searching.set(false);
      },
    });
  }

  private refreshProduct(productId: number): void {
    this.api.getProduct(productId).subscribe({
      next: (product) => this.selected.set(product),
      error: () => undefined,
    });
  }

  private loadHistory(productId: number): void {
    this.historyLoading.set(true);
    this.api.getStockTransactions({ productId, pageSize: 8 }).subscribe({
      next: (result) => {
        this.history.set(result.items);
        this.historyLoading.set(false);
      },
      error: () => {
        this.history.set([]);
        this.historyLoading.set(false);
      },
    });
  }
}

function newRequestId(): string {
  const cryptoApi = globalThis.crypto as Crypto | undefined;
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') {
    return cryptoApi.randomUUID();
  }

  // 非安全上下文的兜底：仍然生成 UUID v4 形态的字符串。
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (char) => {
    const random = (Math.random() * 16) | 0;
    const value = char === 'x' ? random : (random & 0x3) | 0x8;
    return value.toString(16);
  });
}
