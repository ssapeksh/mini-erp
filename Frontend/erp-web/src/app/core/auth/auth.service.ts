import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, firstValueFrom, map, of, shareReplay, throwError } from 'rxjs';

import { API_BASE_URL, SKIP_ERROR_TOAST } from '../api/api.config';
import { ApiResponse } from '../api/api.models';
import { CurrentUserStore } from '../state/current-user.store';
import { AuthResponse, UserInfo } from './auth.models';

const REFRESH_KEY = 'minierp.refreshToken';

/**
 * Authentication state with SIGNALS.
 *   signal()   - a value that notifies readers when it changes
 *   computed() - derived from other signals, recalculated only when they change
 *   effect()   - side effect that re-runs when the signals it reads change
 *
 * TOKEN STORAGE (interview question "where do you keep the JWT?"):
 *   - access token: in MEMORY only (this signal). Any script on the page can
 *     read localStorage, so an XSS bug could steal a token stored there.
 *   - refresh token: sessionStorage (per tab, cleared when the tab closes), so
 *     a page refresh can get a new access token. The safest option is an
 *     HttpOnly cookie that JavaScript can't read at all.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);       // inject() instead of constructor parameters
  private readonly router = inject(Router);
  private readonly api = inject(API_BASE_URL);
  private readonly userStore = inject(CurrentUserStore);

  private readonly _accessToken = signal<string | null>(null);
  private readonly _user = signal<UserInfo | null>(null);

  readonly user = this._user.asReadonly();
  readonly isLoggedIn = computed(() => this._user() !== null && this._accessToken() !== null);
  readonly permissions = computed(() => new Set(this._user()?.permissions ?? []));
  readonly initials = computed(() =>
    (this._user()?.fullName ?? '').split(' ').map((p) => p[0]).join('').slice(0, 2).toUpperCase());

  /** One refresh at a time: parallel 401s share the same in-flight request. */
  private refreshInFlight$: Observable<string> | null = null;

  constructor() {
    // effect: mirror the signal into the BehaviorSubject store (signal -> RxJS world)
    effect(() => this.userStore.set(this._user()));
  }

  get accessToken(): string | null {
    return this._accessToken();
  }

  hasPermission(code: string): boolean {
    return this.permissions().has(code);
  }

  hasAnyPermission(...codes: string[]): boolean {
    return codes.some((c) => this.permissions().has(c));
  }

  login(userName: string, password: string): Observable<UserInfo> {
    return this.http
      .post<ApiResponse<AuthResponse>>(`${this.api}/api/v1/auth/login`, { userName, password },
        { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })   // the form shows its own error
      .pipe(map((res) => this.applyTokens(res.data!)));
  }

  /**
   * Exchanges the refresh token for a new pair (the API ROTATES it: the old one
   * stops working). shareReplay makes concurrent callers share one request.
   */
  refresh(): Observable<string> {
    const refreshToken = this.readRefreshToken();
    if (!refreshToken) return throwError(() => new Error('No refresh token'));

    this.refreshInFlight$ ??= this.http
      .post<ApiResponse<AuthResponse>>(`${this.api}/api/v1/auth/refresh`, { refreshToken },
        { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .pipe(
        map((res) => {
          this.applyTokens(res.data!);
          return res.data!.accessToken;
        }),
        finalize(() => (this.refreshInFlight$ = null)),
        shareReplay(1),
      );
    return this.refreshInFlight$;
  }

  /** Called once at start-up (provideAppInitializer): silently restore a session after F5. */
  restoreSession(): Promise<void> {
    if (!this.readRefreshToken()) return Promise.resolve();
    return firstValueFrom(
      this.refresh().pipe(
        map(() => undefined),
        catchError(() => {
          this.clear();
          return of(undefined);
        }),
      ),
    );
  }

  logout(reason?: 'session' | 'expired'): void {
    const hadToken = !!this._accessToken();
    if (hadToken) {
      // tell the API to end the session; ignore failures (we're leaving anyway)
      this.http.post(`${this.api}/api/v1/auth/logout`, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
        .pipe(catchError(() => of(null)))
        .subscribe();
    }
    this.clear();
    void this.router.navigate(['/login'], reason ? { queryParams: { reason } } : {});
  }

  /** Local sign-out without calling the API (used when the API says the session already ended). */
  clear(): void {
    this._accessToken.set(null);
    this._user.set(null);
    try { sessionStorage.removeItem(REFRESH_KEY); } catch { /* storage blocked */ }
  }

  private applyTokens(auth: AuthResponse): UserInfo {
    this._accessToken.set(auth.accessToken);
    this._user.set(auth.user);
    try { sessionStorage.setItem(REFRESH_KEY, auth.refreshToken); } catch { /* storage blocked */ }
    return auth.user;
  }

  private readRefreshToken(): string | null {
    try { return sessionStorage.getItem(REFRESH_KEY); } catch { return null; }
  }
}

