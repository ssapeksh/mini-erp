import { NgClass } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * The MODERN signal API: input() instead of @Input(), computed() instead of
 * ngOnChanges. When `status` changes, `css` and `label` recompute automatically.
 * input.required() fails compilation if the parent forgets to pass it.
 */
@Component({
  selector: 'app-status-badge',
  template: `<span class="pill" [ngClass]="css()">{{ label() }}</span>`,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusBadgeComponent {
  readonly status = input.required<string>();

  readonly css = computed(() => {
    switch (this.status()) {
      case 'Approved': case 'Posted': case 'OK': return 'pill-ok';
      case 'PendingL1': case 'PendingL2': case 'Low': case 'Draft': return 'pill-warn';
      case 'Rejected': case 'Failed': case 'Reorder': case 'Cancelled': return 'pill-bad';
      default: return 'pill-info';
    }
  });

  readonly label = computed(() =>
    ({ PendingL1: 'Pending L1', PendingL2: 'Pending L2' } as Record<string, string>)[this.status()] ?? this.status());
}
