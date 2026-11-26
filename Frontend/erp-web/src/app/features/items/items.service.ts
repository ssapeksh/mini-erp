import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL, SKIP_ERROR_TOAST } from '../../core/api/api.config';
import { ApiResponse, Item, ItemHistory, ItemListItem, ItemQuery, ItemSaveRequest, PagedResult } from '../../core/api/api.models';
import { unwrap } from '../../core/api/lookups.service';

/**
 * HttpClient returns cold OBSERVABLES: nothing is sent until someone subscribes
 * (the async pipe, toSignal, or .subscribe()), and unsubscribing cancels the request.
 * A Promise would start immediately and can't be cancelled.
 */
@Injectable({ providedIn: 'root' })
export class ItemsService {
  private readonly http = inject(HttpClient);
  private readonly api = `${inject(API_BASE_URL)}/api/v1/items`;

  list(q: ItemQuery): Observable<PagedResult<ItemListItem>> {
    let params = new HttpParams()
      .set('sortBy', q.sortBy).set('sortDir', q.sortDir).set('page', q.page).set('pageSize', q.pageSize);
    if (q.search) params = params.set('search', q.search);
    if (q.categoryId) params = params.set('categoryId', q.categoryId);
    return this.http.get<ApiResponse<PagedResult<ItemListItem>>>(this.api, { params }).pipe(unwrap<PagedResult<ItemListItem>>());
  }

  get(id: number): Observable<Item> {
    return this.http.get<ApiResponse<Item>>(`${this.api}/${id}`).pipe(unwrap<Item>());
  }

  history(id: number): Observable<ItemHistory[]> {
    return this.http.get<ApiResponse<ItemHistory[]>>(`${this.api}/${id}/history`).pipe(unwrap<ItemHistory[]>());
  }

  /** For the async validator: errors are handled by the validator, so no toast. */
  isCodeAvailable(code: string, excludeId?: number): Observable<boolean> {
    let params = new HttpParams().set('code', code);
    if (excludeId) params = params.set('excludeId', excludeId);
    return this.http
      .get<ApiResponse<boolean>>(`${this.api}/check-code`, { params, context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .pipe(unwrap<boolean>());
  }

  create(body: ItemSaveRequest): Observable<Item> {
    return this.http.post<ApiResponse<Item>>(this.api, body).pipe(unwrap<Item>());
  }

  /** Optimistic concurrency: If-Match carries the row version we loaded; 409 if someone saved first. */
  update(id: number, body: ItemSaveRequest, rowVersion: string): Observable<Item> {
    const headers = new HttpHeaders({ 'If-Match': `"${rowVersion}"` });
    return this.http.put<ApiResponse<Item>>(`${this.api}/${id}`, { ...body, rowVersion }, { headers }).pipe(unwrap<Item>());
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/${id}`);
  }

  /** CSV needs the Bearer token, so it's fetched as a Blob through HttpClient (a plain <a href> can't send headers). */
  exportCsv(): Observable<Blob> {
    return this.http.get(`${this.api}/export`, { responseType: 'blob' }).pipe(map((b) => b));
  }
}
