import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';
import { API_BASE_URL } from './api.config';
import { ApiResponse, Customer, Lookup, PagedResult } from './api.models';

/** Unwraps ApiResponse<T> -> T. A small reusable RxJS operator (a function returning a function). */
export const unwrap = <T>() => map((res: ApiResponse<T>) => res.data as T);

/**
 * Dropdown data. shareReplay(1) CACHES the last value: the first subscriber
 * triggers the HTTP call; later subscribers (other screens) reuse the result
 * instead of calling the API again. The server output-caches these too.
 */
@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly http = inject(HttpClient);
  private readonly api = `${inject(API_BASE_URL)}/api/v1/lookups`;

  readonly categories$: Observable<Lookup[]> = this.http
    .get<ApiResponse<Lookup[]>>(`${this.api}/categories`)
    .pipe(unwrap<Lookup[]>(), shareReplay({ bufferSize: 1, refCount: false }));

  readonly warehouses$: Observable<Lookup[]> = this.http
    .get<ApiResponse<Lookup[]>>(`${this.api}/warehouses`)
    .pipe(unwrap<Lookup[]>(), shareReplay({ bufferSize: 1, refCount: false }));

  customers(search = '', page = 1, pageSize = 50): Observable<PagedResult<Customer>> {
    const params = new HttpParams().set('search', search).set('page', page).set('pageSize', pageSize).set('sortBy', 'name');
    return this.http.get<ApiResponse<PagedResult<Customer>>>(`${this.api}/customers`, { params })
      .pipe(unwrap<PagedResult<Customer>>());
  }
}
