import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, numberAttribute, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { EMPTY, Observable, Subject, catchError, exhaustMap, finalize, merge, switchMap, tap } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { ToastService } from '../../core/ui/toast.service';
import { CardComponent } from '../../shared/components/card.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge.component';
import { OrdersService } from './orders.service';

type Action = 'submit' | 'approve' | 'reject' | 'despatch' | 'delete';

/**
 * Order detail + workflow buttons.
 * Every button click goes into ONE Subject; exhaustMap IGNORES new clicks
 * while a request is still running, so a double click can't approve twice.
 * (switchMap would cancel the first request; concatMap would queue the second;
 *  mergeMap would run both: all wrong for "do this once".)
 */
@Component({
  selector: 'app-order-detail',
  imports: [RouterLink, ReactiveFormsModule, CurrencyPipe, DatePipe, DecimalPipe, CardComponent, StatusBadgeComponent],
  templateUrl: './order-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetailComponent {
  private readonly api = inject(OrdersService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  /** ':id' route param, converted from string by numberAttribute. */
  readonly id = input.required({ transform: numberAttribute });

  protected readonly comments = new FormControl('', { nonNullable: true });
  protected readonly busy = signal<Action | null>(null);
  private readonly actions$ = new Subject<Action>();
  private readonly reload$ = new Subject<void>();

  /**
   * toObservable(this.id) emits once the input HAS a value (it runs in an effect),
   * so the first load happens after the router sets :id. Reading this.id() any
   * earlier (e.g. startWith() in a field initializer) throws NG0950.
   */
  protected readonly order = toSignal(
    merge(toObservable(this.id), this.reload$).pipe(
      switchMap(() => this.api.get(this.id()).pipe(catchError(() => EMPTY))),
    ),
    { initialValue: null },
  );

  /** What may THIS user do with THIS order right now (the API enforces the same rules). */
  protected readonly can = computed(() => {
    const o = this.order();
    const has = (p: string) => this.auth.hasPermission(p);
    return {
      submit: o?.status === 'Draft' && has(Permissions.OrdersCreate),
      delete: o?.status === 'Draft' && has(Permissions.OrdersCreate),
      approve: (o?.pendingLevel === 1 && has(Permissions.OrdersApproveL1)) || (o?.pendingLevel === 2 && has(Permissions.OrdersApproveL2)),
      despatch: o?.status === 'Approved' && has(Permissions.OrdersDespatch),
    };
  });

  constructor() {
    this.actions$.pipe(
      exhaustMap((action) => {
        this.busy.set(action);
        return this.run(action).pipe(
          catchError(() => EMPTY),             // error toast comes from the interceptor; keep the stream alive
          finalize(() => this.busy.set(null)),
        );
      }),
      takeUntilDestroyed(),                    // auto-unsubscribe when the component is destroyed
    ).subscribe();
  }

  act(action: Action): void {
    if (action === 'reject' && this.comments.value.trim().length < 3) {
      this.toast.error('Enter a reason (at least 3 characters) to reject.');
      return;
    }
    if (action === 'delete' && !confirm('Delete this draft order?')) return;
    this.actions$.next(action);
  }

  private run(action: Action): Observable<unknown> {
    const id = this.id();
    const comment = this.comments.value.trim() || null;
    const done = (msg: string) => tap(() => {
      this.toast.success(msg);
      this.comments.reset();
      this.reload$.next();
    });
    switch (action) {
      case 'submit': return this.api.submit(id).pipe(done('Submitted for level-1 approval.'));
      case 'approve': return this.api.approve(id, comment).pipe(done('Approved.'));
      case 'reject': return this.api.reject(id, comment ?? '').pipe(done('Rejected.'));
      case 'despatch': return this.api.despatch(id).pipe(done('Despatched: stock and ledger updated.'));
      case 'delete': return this.api.delete(id).pipe(tap(() => {
        this.toast.success('Draft deleted.');
        void this.router.navigate(['/orders']);
      }));
    }
  }

}
