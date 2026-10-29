import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, map, tap } from 'rxjs';
import { LoginResponse, UserInfo } from './api.models';
import { RawLoginResponse, normalizeLogin } from './api.normalize';

interface Session {
  accessToken: string;
  expiresAtUtc: string;
  user: UserInfo;
}

const STORAGE_KEY = 'inventory.session.v1';

/**
 * 会话管理：本地只保存 JWT 与用户名/角色，绝不保存密码。
 * 刷新页面后从 localStorage 恢复；令牌过期则视为未登录。
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  private readonly sessionSignal = signal<Session | null>(readStoredSession());

  readonly session = this.sessionSignal.asReadonly();

  readonly user = computed(() => this.sessionSignal()?.user ?? null);

  readonly roles = computed(() => this.user()?.roles ?? []);

  readonly isAdmin = computed(() => this.roles().includes('Admin'));

  readonly isAuthenticated = computed(() => {
    const session = this.sessionSignal();
    return !!session && new Date(session.expiresAtUtc).getTime() > Date.now();
  });

  login(userName: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<RawLoginResponse>('/api/auth/login', { userName, password })
      .pipe(
        map((raw) => normalizeLogin(raw)),
        tap((response) => {
          const session: Session = {
            accessToken: response.accessToken,
            expiresAtUtc: response.expiresAtUtc,
            user: response.user,
          };
          this.sessionSignal.set(session);
          localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
        }),
      );
  }

  logout(redirect = true): void {
    this.sessionSignal.set(null);
    localStorage.removeItem(STORAGE_KEY);
    if (redirect) {
      void this.router.navigate(['/login']);
    }
  }

  token(): string | null {
    return this.sessionSignal()?.accessToken ?? null;
  }

  hasRole(role: string): boolean {
    return this.roles().includes(role);
  }
}

function readStoredSession(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as Session;
    if (!parsed?.accessToken || !parsed?.expiresAtUtc) {
      return null;
    }

    if (new Date(parsed.expiresAtUtc).getTime() <= Date.now()) {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}
