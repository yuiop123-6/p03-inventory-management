import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** 统一附加 Bearer 令牌；任何受保护请求返回 401 时清空会话并回到登录页。 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.token();
  const isLoginCall = request.url.includes('/auth/login');

  const authorized = token && !isLoginCall
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !isLoginCall) {
        auth.logout(false);
        const returnUrl = router.url;
        void router.navigate(['/login'], {
          queryParams: returnUrl && returnUrl !== '/login' ? { returnUrl } : {},
        });
      }

      return throwError(() => error);
    }),
  );
};
