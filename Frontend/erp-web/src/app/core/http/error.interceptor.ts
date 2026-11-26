import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { EMPTY, catchError, switchMap, throwError } from 'rxjs';
import { SKIP_ERROR_TOAST } from '../api/api.config';
import { ApiResponse } from '../api/api.models';
import { AuthService } from '../auth/auth.service';
import { ToastService } from '../ui/toast.service';

/**
 * Central HTTP error handling (no try/catch in every component):
 *  - 401 + "Token-Expired: true"  -> refresh the token once, then RETRY the request
 *  - 401 otherwise (session ended, logged in elsewhere, revoked) -> back to login, complete quietly
 *  - 403 / 404 / 409 / 422 / 429 / 5xx -> toast with the API's message
 *  - status 0 -> the API is unreachable (or CORS blocked the response)
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) return throwError(() => error);
      const isAuthCall = /\/auth\/(login|refresh|logout)$/.test(req.url);

      if (error.status === 401 && !isAuthCall) {
        if (error.headers.get('Token-Expired') === 'true') {
          // switchMap: when the refresh emits a new token, switch to the retried request
          return auth.refresh().pipe(
            switchMap((token) => next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }))),
            catchError(() => {
              auth.clear();
              auth.logout('expired');
              return EMPTY;
            }),
          );
        }
        auth.clear();
        auth.logout('session');
        // We're leaving for /login anyway: complete quietly instead of erroring every
        // pending subscriber (async pipes would log "ERROR HttpErrorResponse").
        return EMPTY;
      }

      if (!req.context.get(SKIP_ERROR_TOAST)) toast.error(messageOf(error));
      return throwError(() => error);
    }),
  );
};

/** Best human-readable message from an error response. */
export function messageOf(error: HttpErrorResponse): string {
  if (error.status === 0) return 'Cannot reach the server. Is Erp.Api running on port 5101?';
  const body = error.error as ApiResponse<unknown> | null;
  if (body?.errors) {
    const first = Object.entries(body.errors)[0];
    if (first) return `${first[0]}: ${first[1][0]}`;
  }
  if (body?.message) return body.message;
  if (error.status === 429) return 'Too many requests, please wait a moment.';
  return `Request failed (${error.status}).`;
}
