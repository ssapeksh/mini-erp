import { AsyncPipe, CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { BehaviorSubject, Observable, Subscription, combineLatest, interval, map, shareReplay, switchMap } from 'rxjs';

import { StockSummary } from '../../core/api/api.models';
import { LookupsService } from '../../core/api/lookups.service';
import { ReportsService } from '../../core/api/reports.service';
import { Permissions } from '../../core/auth/permissions';
import { ToastService } from '../../core/ui/toast.service';
import { CardComponent } from '../../shared/components/card.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge.component';
import { HasPermissionDirective } from '../../shared/directives/has-permission.directive';

/**
 * Stock page written in the CLASSIC RxJS + async-pipe style (no signals for
 * the data), so both styles exist in the codebase:
 *  - filters are BehaviorSubjects; combineLatest + switchMap rebuild the list
 *  - the template subscribes with `| async` (auto-unsubscribe)
 *  - ngOnInit starts a 60-second auto-refresh; ngOnDestroy MUST stop it,
 *    otherwise the interval keeps firing after the user leaves (a memory leak)
 */
@Component({
  selector: 'app-stock',
  imports: [AsyncPipe, CurrencyPipe, DecimalPipe, ReactiveFormsModule, CardComponent, StatusBadgeComponent, HasPermissionDirective],
  templateUrl: './stock.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockComponent implements OnInit, OnDestroy {
  private readonly reports = inject(ReportsService);
  private readonly toast = inject(ToastService);
  protected readonly warehouses$ = inject(LookupsService).warehouses$;
  protected readonly P = Permissions;

  protected readonly warehouse$ = new BehaviorSubject<number | null>(null);
  protected readonly status$ = new BehaviorSubject<string>('');
  private readonly refresh$ = new BehaviorSubject<void>(undefined);
  private autoRefresh?: Subscription;
  protected readonly lastRefresh = signal(new Date());

  protected readonly rows$: Observable<StockSummary[]> = combineLatest([this.warehouse$, this.status$, this.refresh$]).pipe(
    switchMap(([wh, status]) => this.reports.stock(wh, status || null)),
    shareReplay({ bufferSize: 1, refCount: true }),     // two async pipes below share ONE request
  );
  protected readonly totalValue$ = this.rows$.pipe(map((rows) => rows.reduce((s, r) => s + r.stockValue, 0)));

  protected readonly adjustForm = inject(FormBuilder).nonNullable.group({
    itemId: [0, Validators.min(1)],
    warehouseId: [1, Validators.min(1)],
    qty: [0, [Validators.required]],
    reason: ['', [Validators.required, Validators.minLength(3)]],
  });

  ngOnInit(): void {
    this.autoRefresh = interval(60_000).subscribe(() => this.reload());
  }

  ngOnDestroy(): void {
    this.autoRefresh?.unsubscribe();            // manual cleanup (takeUntilDestroyed does this automatically)
  }

  reload(): void {
    this.lastRefresh.set(new Date());
    this.refresh$.next();
  }

  pick(row: StockSummary): void {
    this.adjustForm.patchValue({ itemId: row.itemId, warehouseId: row.warehouseId });
  }

  adjust(): void {
    if (this.adjustForm.invalid || this.adjustForm.controls.qty.value === 0) {
      this.adjustForm.markAllAsTouched();
      return;
    }
    const { itemId, warehouseId, qty, reason } = this.adjustForm.getRawValue();   // destructuring
    this.reports.adjustStock(itemId, warehouseId, qty, reason).subscribe((r) => {
      this.toast.success(`Stock changed from ${r.oldQuantity} to ${r.newQuantity}.`);
      this.adjustForm.reset({ itemId: 0, warehouseId: 1, qty: 0, reason: '' });
      this.reload();
    });
  }
}
