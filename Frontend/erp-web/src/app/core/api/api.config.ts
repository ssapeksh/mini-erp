import { HttpContextToken } from '@angular/common/http';
import { InjectionToken } from '@angular/core';

/**
 * INJECTION TOKEN: lets DI provide a plain value (not a class).
 * Provided in app.config.ts from environment.ts; services inject it.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');

/**
 * HTTP CONTEXT TOKEN: per-request metadata for interceptors.
 * A request sent with SKIP_ERROR_TOAST=true handles its own errors
 * (login form, async validators), so the error interceptor stays quiet.
 */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);
