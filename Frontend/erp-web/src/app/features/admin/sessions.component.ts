import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Subject, merge, switchMap } from 'rxjs';
import { ReportsService } from '../../core/api/reports.service';
import { Session } from '../../core/api/api.models';
import { ToastService } from '../../core/ui/toast.service';
import { CardComponent } from '../../shared/components/card.component';

/** Session Insight: who is signed in, from where, and a button to end a session (the user's next request gets 401). */
@Component({
  selector: 'app-sessions',
  imports: [DatePipe, CardComponent],
  template: `
    <div class="page-header">
      <h1>Sessions</h1>
      <label class="check"><input type="checkbox" [checked]="activeOnly()" (change)="activeOnly.set(!activeOnly())" /> Active only</label>
    </div>
    <app-card>
      <div class="table-wrap">
        <table class="table">
          <thead><tr><th>User</th><th>IP</th><th>Browser</th><th>Started</th><th>Last activity</th><th>Ended</th><th></th></tr></thead>
          <tbody>
            @for (s of sessions(); track s.id) {
              <tr>
                <td><strong>{{ s.userName }}</strong> <span class="muted small">{{ s.fullName }}</span></td>
                <td class="mono">{{ s.ipAddress }}</td>
                <td class="ellipsis" [title]="s.userAgent ?? ''">{{ s.userAgent }}</td>
                <td>{{ s.startedOn | date: 'dd-MMM HH:mm' }}</td>
                <td>{{ s.lastActivityOn | date: 'dd-MMM HH:mm:ss' }}</td>
                <td>{{ s.endedOn ? (s.endedOn | date: 'dd-MMM HH:mm') + ' · ' + s.endReason : '' }}</td>
                <td>@if (!s.endedOn) { <button type="button" class="btn-link danger" (click)="revoke(s)">End session</button> }</td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="muted center">No sessions.</td></tr>
            }
          </tbody>
        </table>
      </div>
    </app-card>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SessionsComponent {
  private readonly api = inject(ReportsService);
  private readonly toast = inject(ToastService);
  protected readonly activeOnly = signal(true);
  private readonly reload$ = new Subject<void>();

  protected readonly sessions = toSignal(
    merge(toObservable(this.activeOnly), this.reload$).pipe(   // toObservable emits the initial value itself
      switchMap(() => this.api.sessions(this.activeOnly())),
    ),
    { initialValue: [] as Session[] },
  );

  revoke(s: Session): void {
    if (!confirm(`End ${s.userName}'s session? Their next request will be rejected.`)) return;
    this.api.revokeSession(s.id).subscribe(() => {
      this.toast.success(`Session of ${s.userName} ended.`);
      this.reload$.next();
    });
  }
}
