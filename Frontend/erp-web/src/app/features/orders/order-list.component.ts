import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { OrderStatus } from '../../core/api/api.models';
import { Permissions } from '../../core/auth/permissions';
import { CardComponent } from '../../shared/components/card.component';
import { PagerComponent } from '../../shared/components/pager.component';
import { HasPermissionDirective } from '../../shared/directives/has-permission.directive';
import { OrderListStore } from './order-list.store';
import { OrderRowComponent } from './order-row.component';

@Component({
  selector: 'app-order-list',
  imports: [FormsModule, RouterLink, CardComponent, PagerComponent, OrderRowComponent, HasPermissionDirective],
  // HIERARCHICAL INJECTOR: providing the store HERE gives this component (and its
  // children) their own instance, destroyed with the component. providedIn:'root'
  // services are app-wide singletons instead.
  providers: [OrderListStore],
  template: `
    <div class="page-header">
      <h1>Orders</h1>
      <div class="actions">
        <button *appHasPermission="P.OrdersCreate" type="button" class="btn" [disabled]="store.selectedCount() === 0"
                (click)="store.submitSelected()">Submit selected ({{ store.selectedCount() }})</button>
        <a *appHasPermission="P.OrdersCreate" class="btn btn-primary" routerLink="new">+ New order</a>
      </div>
    </div>

    <app-card>
      <div class="filters">
        <input type="search" class="grow" placeholder="Search order no or customer…" aria-label="Search orders"
               [ngModel]="store.query().search" (ngModelChange)="store.setFilter({ search: $event })"
               [ngModelOptions]="{ updateOn: 'blur' }" (keyup.enter)="$any($event.target).blur()" />
        <select aria-label="Status" [ngModel]="store.query().status" (ngModelChange)="store.setFilter({ status: $event })">
          <option value="">All statuses</option>
          @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
        </select>
        @if (store.loading()) { <span class="muted small">Loading…</span> }
      </div>

      <div class="table-wrap">
        <table class="table">
          <thead>
            <tr><th></th><th>Order no</th><th>Date</th><th>Customer</th><th>WH</th><th class="num">Lines</th><th class="num">Total</th><th>Status</th></tr>
          </thead>
          <tbody>
            @for (o of store.orders(); track o.id) {
              <tr app-order-row [order]="o" [selected]="store.selected().includes(o.id)" (toggled)="store.toggle($event)"></tr>
            } @empty {
              <tr><td colspan="8" class="muted center">No orders.</td></tr>
            }
          </tbody>
        </table>
      </div>

      @if (store.result(); as r) {
        <app-pager [page]="r.page" [totalPages]="r.totalPages" [totalCount]="r.totalCount"
                   (pageChange)="store.setFilter({ page: $event })" />
      }
    </app-card>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderListComponent {
  protected readonly store = inject(OrderListStore);
  protected readonly P = Permissions;
  protected readonly statuses: OrderStatus[] =
    ['Draft', 'PendingL1', 'PendingL2', 'Approved', 'Rejected', 'Despatched', 'Posted', 'Failed'];
}
