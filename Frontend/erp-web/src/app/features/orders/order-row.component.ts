import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, Input, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OrderSummary } from '../../core/api/api.models';
import { StatusBadgeComponent } from '../../shared/components/status-badge.component';

/**
 * Child component rendering one table row. ATTRIBUTE SELECTOR 'tr[app-order-row]'
 * so it can sit directly inside <tbody> (a custom <app-order-row> tag there
 * would be invalid HTML).
 *
 * Parent -> child: @Input() order (decorator) and selected = input() (signal);
 * both styles on purpose.
 * Child -> parent: toggled = output<number>() (the signal-era @Output/EventEmitter).
 */
@Component({
  selector: 'tr[app-order-row]',
  imports: [RouterLink, CurrencyPipe, DatePipe, StatusBadgeComponent],
  template: `
    <td><input type="checkbox" [checked]="selected()" (change)="toggled.emit(order.id)" [attr.aria-label]="'Select ' + order.orderNo" /></td>
    <td class="mono"><a [routerLink]="[order.id]">{{ order.orderNo }}</a></td>
    <td>{{ order.orderDate | date: 'dd-MMM-yyyy' }}</td>
    <td>{{ order.customerName }}</td>
    <td>{{ order.warehouse }}</td>
    <td class="num">{{ order.lineCount }}</td>
    <td class="num">{{ order.totalAmount | currency: 'INR' }}</td>
    <td><app-status-badge [status]="order.status" /></td>
  `,
  host: { '[class.selected]': 'selected()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderRowComponent {
  @Input({ required: true }) order!: OrderSummary;     // '!' : definitely assigned (required input)
  readonly selected = input(false);
  readonly toggled = output<number>();
}
