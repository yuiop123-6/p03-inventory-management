import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeFailure } from '../../core/api.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  template: `
    <div class="login-page">
      <section class="login-hero" aria-label="Product introduction">
        <div class="hero-brand">
          <span class="brand-mark" aria-hidden="true">IM</span>
          <span class="hero-brand-name">Inventory Management</span>
        </div>

        <p class="hero-kicker">Warehouse operations portal</p>
        <h1>Stock you can trust, tracked to the last unit.</h1>
        <p class="hero-copy">
          Products, categories, IN / OUT / ADJUST movements, low-stock alerts and reports — one
          console for admins and operators.
        </p>

        <div class="hero-demo">
          <strong>Demo accounts</strong>
          <div>admin / Admin&#64;12345 — full access</div>
          <div>operator / Operator&#64;12345 — IN / OUT only</div>
        </div>
      </section>

      <section class="login-panel-wrap">
        <div class="login-card">
          <h2>Sign in</h2>
          <p class="muted login-sub">Use your warehouse account to continue.</p>

          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <div class="field">
              <label for="userName">Username</label>
              <input
                id="userName"
                type="text"
                formControlName="userName"
                autocomplete="username"
                [attr.aria-invalid]="form.controls.userName.invalid && form.controls.userName.touched"
              />
              @if (form.controls.userName.touched && form.controls.userName.invalid) {
                <span class="field-error">Username is required.</span>
              }
            </div>

            <div class="field">
              <label for="password">Password</label>
              <input
                id="password"
                type="password"
                formControlName="password"
                autocomplete="current-password"
                [attr.aria-invalid]="form.controls.password.invalid && form.controls.password.touched"
              />
              @if (form.controls.password.touched && form.controls.password.invalid) {
                <span class="field-error">Password is required.</span>
              }
            </div>

            @if (errorMessage()) {
              <div class="alert alert-error" role="alert">{{ errorMessage() }}</div>
            }

            <button class="btn login-submit" type="submit" [disabled]="submitting()">
              {{ submitting() ? 'Signing in…' : 'Sign in' }}
            </button>
          </form>
        </div>
      </section>
    </div>
  `,
  styles: [
    `
      .login-page {
        min-height: 100vh;
        display: grid;
        grid-template-columns: minmax(0, 1.15fr) minmax(0, 1fr);
      }

      /* 深色品牌区：参考站 hero 的墨蓝底 + 橙色眉题 */
      .login-hero {
        background: var(--ink);
        background-image: radial-gradient(
          circle at 85% 12%,
          rgba(244, 96, 28, 0.14) 0%,
          rgba(244, 96, 28, 0) 42%
        );
        color: #fff;
        padding: 56px 56px 40px;
        display: flex;
        flex-direction: column;
        gap: 18px;
        border-bottom: 3px solid var(--primary);
      }

      .hero-brand {
        display: flex;
        align-items: center;
        gap: 10px;
      }

      .brand-mark {
        display: grid;
        place-items: center;
        width: 32px;
        height: 32px;
        border-radius: 8px;
        background: var(--primary);
        color: #fff;
        font-family: var(--font-display);
        font-weight: 700;
        font-size: 15px;
        letter-spacing: 0.04em;
      }

      .hero-brand-name {
        font-family: var(--font-display);
        font-weight: 700;
        font-size: 18px;
        letter-spacing: 0.05em;
        text-transform: uppercase;
      }

      .hero-kicker {
        display: flex;
        align-items: center;
        gap: 10px;
        color: var(--primary);
        font-weight: 600;
        font-size: 13px;
        text-transform: uppercase;
        letter-spacing: 0.12em;
        margin-top: 12px;
      }

      .hero-kicker::before {
        content: '';
        width: 26px;
        height: 3px;
        border-radius: 2px;
        background: var(--primary);
      }

      .login-hero h1 {
        font-size: 46px;
        line-height: 1.05;
        max-width: 460px;
        color: #fff;
      }

      .login-hero h1::before {
        display: none;
      }

      .hero-copy {
        color: var(--text-faint);
        font-size: 15px;
        max-width: 430px;
      }

      .hero-demo {
        margin-top: auto;
        padding-top: 18px;
        border-top: 1px solid rgba(255, 255, 255, 0.12);
        font-size: 13px;
        color: var(--text-faint);
        line-height: 1.7;
      }

      .hero-demo strong {
        display: block;
        color: #fff;
        font-size: 12px;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        margin-bottom: 4px;
      }

      /* 白色表单卡 */
      .login-panel-wrap {
        display: grid;
        place-items: center;
        padding: 40px 24px;
        background: var(--bg);
      }

      .login-card {
        width: min(420px, 100%);
        background: var(--surface);
        border: 1px solid var(--border);
        border-top: 3px solid var(--primary);
        border-radius: var(--radius);
        box-shadow: var(--shadow-md);
        padding: 30px 28px;
        display: flex;
        flex-direction: column;
        gap: 16px;
      }

      .login-card h2 {
        font-size: 26px;
      }

      .login-sub {
        font-size: 13px;
        margin-top: -10px;
      }

      form {
        display: flex;
        flex-direction: column;
        gap: 14px;
      }

      .login-submit {
        width: 100%;
        margin-top: 4px;
        min-height: 46px;
      }

      @media (max-width: 940px) {
        .login-page {
          grid-template-columns: minmax(0, 1fr);
        }

        .login-hero {
          padding: 28px 22px;
          gap: 12px;
        }

        .login-hero h1 {
          font-size: 30px;
        }

        .hero-copy {
          font-size: 14px;
        }

        .hero-demo {
          margin-top: 6px;
        }

        .login-panel-wrap {
          padding: 26px 16px 40px;
        }
      }
    `,
  ],
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    userName: ['', [Validators.required]],
    password: ['', [Validators.required]],
  });

  protected submit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { userName, password } = this.form.getRawValue();

    this.auth.login(userName, password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        void this.router.navigateByUrl(returnUrl && returnUrl.startsWith('/') ? returnUrl : '/dashboard');
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(
          error && typeof error === 'object' && 'status' in error && error.status === 401
            ? 'Invalid username or password.'
            : describeFailure(error),
        );
      },
    });
  }
}
