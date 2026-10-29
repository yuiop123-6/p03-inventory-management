import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ToastService } from '../core/toast.service';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <header class="topbar">
        <div class="brand">
          <span class="brand-mark" aria-hidden="true">IM</span>
          <span class="brand-text">Inventory Management</span>
        </div>

        <nav class="nav" aria-label="Main navigation">
          <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
          <a routerLink="/products" routerLinkActive="active">Products</a>
          <!-- 需求 02/06：操作员无权管理分类，菜单项按角色动态渲染 -->
          @if (auth.isAdmin()) {
            <a routerLink="/categories" routerLinkActive="active">Categories</a>
          }
          <a routerLink="/stock" routerLinkActive="active">Stock</a>
          <a routerLink="/reports" routerLinkActive="active">Reports</a>
        </nav>

        <div class="account">
          <span class="account-name">{{ auth.user()?.userName }}</span>
          <span class="role-chip" [class.admin]="auth.isAdmin()">
            {{ auth.isAdmin() ? 'Admin' : 'Operator' }}
          </span>
          <button type="button" class="btn btn-ghost btn-sm" (click)="logout()">Sign out</button>
        </div>
      </header>

      <main class="content">
        <router-outlet />
      </main>

      <div class="toast-stack" role="status" aria-live="polite">
        @for (toast of toastService.toasts(); track toast.id) {
          <div class="toast" [class.toast-error]="toast.type === 'error'" [class.toast-success]="toast.type === 'success'">
            <span>{{ toast.message }}</span>
            <button type="button" class="toast-close" (click)="toastService.dismiss(toast.id)" aria-label="Dismiss">×</button>
          </div>
        }
      </div>
    </div>
  `,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly toastService = inject(ToastService);
  private readonly router = inject(Router);

  protected logout(): void {
    this.auth.logout(false);
    this.toastService.info('You have been signed out.');
    void this.router.navigate(['/login']);
  }
}
