import {
  ApplicationConfig,
  DEFAULT_CURRENCY_CODE,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { environment } from '../environments/environment';
import { API_BASE_URL } from './core/api/api.config';
import { AuthService } from './core/auth/auth.service';
import { authInterceptor } from './core/http/auth.interceptor';
import { errorInterceptor } from './core/http/error.interceptor';
import { loadingInterceptor } from './core/http/loading.interceptor';

/**
 * App-wide providers (the standalone replacement for AppModule).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // ZONELESS change detection: no zone.js patching every setTimeout/click.
    // Signals, the async pipe and events tell Angular exactly when to re-render.
    provideZonelessChangeDetection(),

    // withComponentInputBinding: route params / resolver data arrive as component inputs
    provideRouter(routes, withComponentInputBinding()),

    // HttpClient with FUNCTIONAL interceptors. Order matters: requests pass
    // loading -> auth -> error in this order, responses come back in reverse.
    provideHttpClient(withFetch(), withInterceptors([loadingInterceptor, authInterceptor, errorInterceptor])),

    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
    { provide: LOCALE_ID, useValue: 'en-IN' },
    { provide: DEFAULT_CURRENCY_CODE, useValue: 'INR' },

    // Runs before the first route is activated: the access token lives only in
    // memory, so after a page refresh we use the refresh token to get a new one.
    provideAppInitializer(() => inject(AuthService).restoreSession()),
  ],
};
