import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Endpoints where a 401 is a legitimate answer rather than an expired session.
 *
 * /login answers 401 for bad credentials and for a pending/blocked account; /me answers 401 to mean
 * "not signed in". Redirecting on those would fight the very screens that need to read the failure,
 * and on /me it would loop: guard -> /me -> 401 -> redirect -> guard.
 */
const EXPECTED_401_PATHS = ['/api/auth/login', '/api/auth/me'];

/**
 * Reacts to session expiry in one place, so no future screen has to remember to.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      const isExpiredSession =
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !EXPECTED_401_PATHS.some((path) => req.url.startsWith(path));

      if (isExpiredSession) {
        auth.clear();
        void router.navigate(['/login']);
      }

      return throwError(() => error);
    }),
  );
};
