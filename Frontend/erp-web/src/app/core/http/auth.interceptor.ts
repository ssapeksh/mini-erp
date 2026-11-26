import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { API_BASE_URL } from '../api/api.config';
import { AuthService } from '../auth/auth.service';

/**
 * Adds "Authorization: Bearer <token>" to every call to OUR API.
 * HttpRequest is immutable, so the interceptor clones it with the extra header.
 * Never send the token to other origins (it would leak to third parties).
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).accessToken;
  const isOurApi = req.url.startsWith(inject(API_BASE_URL));
  const isAuthCall = /\/auth\/(login|refresh)$/.test(req.url);

  return next(token && isOurApi && !isAuthCall
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req);
};
