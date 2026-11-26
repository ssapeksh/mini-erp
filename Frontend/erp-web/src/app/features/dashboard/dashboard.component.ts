import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EMPTY, catchError } from 'rxjs';
import { ReportsService } from '../../core/api/reports.service';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { CardComponent } from '../../shared/components/card.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge.component';

/**
 * Dashboard:
 *  - toSignal() turns the HTTP Observable into a signal (subscribes and
 *    unsubscribes automatically with the component)
 *  - computed() derives the chart data
 *  - @defer (on viewport) lazy-loads the below-the-fold panels only when they
 *    scroll into view, with @placeholder / @loading templates
 * Users without Reports.View get quick links instead of numbers.
 */
@Component({
  selector: 'app-dashboard',
  imports: [CardComponent, StatusBadgeComponent, CurrencyPipe, DecimalPipe, RouterLink],
  templateUrl: './dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardComponent {
  private readonly reports = inject(ReportsService);
  protected readonly auth = inject(AuthService);
  protected readonly canSeeReports = this.auth.hasPermission(Permissions.ReportsView);

  protected readonly data = toSignal(
    this.canSeeReports ? this.reports.dashboard().pipe(catchError(() => EMPTY)) : EMPTY,
    { initialValue: null },
  );

  protected readonly totalOrders = computed(() =>
    this.data()?.ordersByStatus.reduce((sum, s) => sum + s.count, 0) ?? 0);

  /** Bar heights as % of the best month (a CSS-only chart, no library). */
  protected readonly bars = computed(() => {
    const months = this.data()?.recentMonths ?? [];
    const max = Math.max(1, ...months.map((m) => m.total));
    return months.map((m) => ({ ...m, height: Math.round((m.total / max) * 100) }));
  });
}
