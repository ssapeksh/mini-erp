import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api.config';
import { ApiResponse, Dashboard, Session, StockSummary } from './api.models';
import { unwrap } from './lookups.service';

/** Smaller read-only endpoints: dashboard, stock, admin sessions. */
@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly api = `${inject(API_BASE_URL)}/api/v1`;

  dashboard(): Observable<Dashboard> {
    return this.http.get<ApiResponse<Dashboard>>(`${this.api}/reports/dashboard`).pipe(unwrap<Dashboard>());
  }

  stock(warehouseId?: number | null, status?: string | null): Observable<StockSummary[]> {
    let params = new HttpParams();
    if (warehouseId) params = params.set('warehouseId', warehouseId);
    if (status) params = params.set('status', status);
    return this.http.get<ApiResponse<StockSummary[]>>(`${this.api}/stock`, { params }).pipe(unwrap<StockSummary[]>());
  }

  adjustStock(itemId: number, warehouseId: number, qty: number, reason: string): Observable<{ oldQuantity: number; newQuantity: number }> {
    return this.http
      .post<ApiResponse<{ oldQuantity: number; newQuantity: number }>>(`${this.api}/stock/adjustments`, { itemId, warehouseId, qty, reason })
      .pipe(unwrap<{ oldQuantity: number; newQuantity: number }>());
  }

  sessions(activeOnly: boolean): Observable<Session[]> {
    return this.http.get<ApiResponse<Session[]>>(`${this.api}/admin/sessions`, { params: { activeOnly } }).pipe(unwrap<Session[]>());
  }

  revokeSession(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/admin/sessions/${id}`);
  }
}
