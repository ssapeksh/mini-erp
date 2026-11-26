import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api.config';
import { ApiResponse, CreateOrderRequest, DespatchResult, Order, OrderQuery, OrderSummary, PagedResult } from '../../core/api/api.models';
import { unwrap } from '../../core/api/lookups.service';

@Injectable({ providedIn: 'root' })
export class OrdersService {
  private readonly http = inject(HttpClient);
  private readonly api = `${inject(API_BASE_URL)}/api/v1/orders`;

  list(q: OrderQuery): Observable<PagedResult<OrderSummary>> {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.status) params = params.set('status', q.status);
    if (q.search) params = params.set('search', q.search);
    return this.http.get<ApiResponse<PagedResult<OrderSummary>>>(this.api, { params }).pipe(unwrap<PagedResult<OrderSummary>>());
  }

  get(id: number): Observable<Order> {
    return this.http.get<ApiResponse<Order>>(`${this.api}/${id}`).pipe(unwrap<Order>());
  }

  /**
   * POST isn't idempotent: a retry after a timeout could create a second order.
   * The same Idempotency-Key for the same submission makes the API replay the
   * first response instead. The form generates one key per submit attempt.
   */
  create(body: CreateOrderRequest, idempotencyKey: string): Observable<Order> {
    const headers = new HttpHeaders({ 'Idempotency-Key': idempotencyKey });
    return this.http.post<ApiResponse<Order>>(this.api, body, { headers }).pipe(unwrap<Order>());
  }

  submit(id: number): Observable<Order> {
    return this.http.post<ApiResponse<Order>>(`${this.api}/${id}/submit`, {}).pipe(unwrap<Order>());
  }

  approve(id: number, comments: string | null): Observable<Order> {
    return this.http.post<ApiResponse<Order>>(`${this.api}/${id}/approve`, { comments }).pipe(unwrap<Order>());
  }

  reject(id: number, comments: string): Observable<Order> {
    return this.http.post<ApiResponse<Order>>(`${this.api}/${id}/reject`, { comments }).pipe(unwrap<Order>());
  }

  despatch(id: number): Observable<DespatchResult> {
    return this.http.post<ApiResponse<DespatchResult>>(`${this.api}/${id}/despatch`, {}).pipe(unwrap<DespatchResult>());
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/${id}`);
  }
}
