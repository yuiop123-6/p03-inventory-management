import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService, describeFailure } from '../../core/api.service';
import { Dashboard } from '../../core/api.models';

interface ChartBar {
  x: number;
  y: number;
  width: number;
  height: number;
  label: string;
  value: number;
  negative: boolean;
  centerX: number;
  valueY: number;
}

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DatePipe],
  template: `
    <div class="page-head">
      <div>
        <h1>Dashboard</h1>
        <p>Live snapshot of stock health, recent movements and the last 7 days.</p>
      </div>
      <div class="page-actions">
        <a class="btn btn-ghost" routerLink="/reports">Open reports</a>
        <a class="btn" routerLink="/stock">Record stock movement</a>
      </div>
    </div>

    @if (loading()) {
      <div class="grid-metrics">
        @for (item of [1, 2, 3, 4]; track item) {
          <div class="metric">
            <div class="skeleton" style="width: 60%"></div>
            <div class="skeleton" style="width: 40%; height: 26px; margin-top: 12px"></div>
          </div>
        }
      </div>
      <div class="card card-pad">
        <div class="skeleton" style="width: 100%; height: 180px"></div>
      </div>
    } @else if (errorMessage()) {
      <div class="card card-pad">
        <div class="alert alert-error" role="alert">{{ errorMessage() }}</div>
        <button class="btn btn-ghost" type="button" style="margin-top: 12px" (click)="load()">Retry</button>
      </div>
    } @else if (data(); as dashboard) {
      <div class="grid-metrics">
        <div class="metric">
          <div class="metric-label">Product types</div>
          <div class="metric-value">{{ dashboard.totalProducts }}</div>
          <div class="metric-hint">Distinct SKUs on file</div>
        </div>
        <div class="metric">
          <div class="metric-label">Total stock</div>
          <div class="metric-value">{{ dashboard.totalStockQuantity }}</div>
          <div class="metric-hint">Units across all products</div>
        </div>
        <div class="metric warn">
          <div class="metric-label">Low stock</div>
          <div class="metric-value">{{ dashboard.lowStockProducts }}</div>
          <div class="metric-hint">0 &lt; stock ≤ threshold</div>
        </div>
        <div class="metric danger">
          <div class="metric-label">Out of stock</div>
          <div class="metric-value">{{ dashboard.outOfStockProducts }}</div>
          <div class="metric-hint">Current stock is 0</div>
        </div>
      </div>

      <div class="split">
        <section class="card">
          <div class="card-head">
            <h2>Recent movements</h2>
            <a routerLink="/reports" class="muted">View all</a>
          </div>

          @if (dashboard.recentTransactions.length === 0) {
            <div class="state">
              <strong>No stock movements yet</strong>
              <p>Record an IN, OUT or ADJUST to see it here.</p>
            </div>
          } @else {
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Reference</th>
                    <th>Product</th>
                    <th>Type</th>
                    <th class="num">Change</th>
                    <th class="num">Balance</th>
                    <th>When (UTC)</th>
                  </tr>
                </thead>
                <tbody>
                  @for (item of dashboard.recentTransactions; track item.id) {
                    <tr>
                      <td class="code">{{ item.transactionReference }}</td>
                      <td>
                        <div>{{ item.productName }}</div>
                        <div class="muted code">{{ item.productCode }}</div>
                      </td>
                      <td>
                        <span class="badge" [class]="badgeClass(item.type)">{{ item.type }}</span>
                      </td>
                      <td class="num">{{ item.quantity }}</td>
                      <td class="num">
                        <span class="muted">{{ item.previousStock }} →</span> <strong>{{ item.newStock }}</strong>
                      </td>
                      <td class="muted">{{ item.createdAt | date: 'yyyy-MM-dd HH:mm' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>

        <section class="card">
          <div class="card-head">
            <h2>Net stock change · last 7 days (UTC)</h2>
          </div>
          <div class="card-pad">
            @if (allZero()) {
              <p class="muted" style="margin-bottom: 10px">No movements in the last 7 days — chart shows zeros.</p>
            }
            <svg class="chart" viewBox="0 0 640 220" role="img" [attr.aria-label]="chartSummary()">
              <line x1="24" y1="150" x2="616" y2="150" stroke="#dcdfd8" stroke-width="1" />
              <line x1="24" y1="90" x2="616" y2="90" stroke="#e9ebe6" stroke-width="1" stroke-dasharray="4 4" />
              <line x1="24" y1="30" x2="616" y2="30" stroke="#e9ebe6" stroke-width="1" stroke-dasharray="4 4" />

              @for (bar of bars(); track bar.label) {
                <rect
                  [attr.x]="bar.x"
                  [attr.y]="bar.y"
                  [attr.width]="bar.width"
                  [attr.height]="bar.height"
                  rx="4"
                  [attr.fill]="bar.negative ? '#c0392b' : '#f4601c'"
                  [attr.opacity]="bar.value === 0 ? 0 : 0.9"
                />
                <text
                  [attr.x]="bar.centerX"
                  [attr.y]="bar.valueY"
                  text-anchor="middle"
                  font-size="11"
                  fill="#5c6672"
                >
                  {{ bar.value }}
                </text>
                <text
                  [attr.x]="bar.centerX"
                  y="172"
                  text-anchor="middle"
                  font-size="11"
                  fill="#98a1ab"
                >
                  {{ bar.label }}
                </text>
              }
            </svg>
            <div class="chart-legend">
              <span><span style="display:inline-block;width:10px;height:10px;background:#f4601c;border-radius:2px;margin-right:6px"></span>Net increase</span>
              <span><span style="display:inline-block;width:10px;height:10px;background:#c0392b;border-radius:2px;margin-right:6px"></span>Net decrease</span>
            </div>
          </div>
        </section>
      </div>
    }
  `,
  styles: [
    `
      .card-head a {
        font-size: 13px;
      }
    `,
  ],
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly data = signal<Dashboard | null>(null);

  protected readonly allZero = computed(() =>
    (this.data()?.stockChangeLast7Days ?? []).every((day) => day.netChange === 0),
  );

  protected readonly bars = computed<ChartBar[]>(() => {
    const trend = this.data()?.stockChangeLast7Days ?? [];
    if (trend.length === 0) {
      return [];
    }

    const peak = Math.max(1, ...trend.map((day) => Math.abs(day.netChange)));
    const baseline = 150;
    const plotHeight = 110;
    const slot = 592 / trend.length;

    return trend.map((day, index) => {
      const height = (Math.abs(day.netChange) / peak) * plotHeight;
      const width = Math.min(52, slot * 0.58);
      const centerX = 24 + slot * index + slot / 2;
      const negative = day.netChange < 0;

      return {
        x: centerX - width / 2,
        y: negative ? baseline : baseline - height,
        width,
        height: day.netChange === 0 ? 0 : height,
        label: day.date.slice(5),
        value: day.netChange,
        negative,
        centerX,
        valueY: negative ? baseline + height + 14 : baseline - height - 6,
      };
    });
  });

  protected readonly chartSummary = computed(() => {
    const trend = this.data()?.stockChangeLast7Days ?? [];
    return trend.map((day) => `${day.date}: ${day.netChange}`).join(', ');
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.api.getDashboard().subscribe({
      next: (dashboard) => {
        this.data.set(dashboard);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(describeFailure(error));
        this.loading.set(false);
      },
    });
  }

  protected badgeClass(type: string): string {
    switch (type) {
      case 'IN':
        return 'badge-in';
      case 'OUT':
        return 'badge-out';
      default:
        return 'badge-adjust';
    }
  }
}
