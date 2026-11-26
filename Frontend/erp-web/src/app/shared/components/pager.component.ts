import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';

/**
 * Pager written the CLASSIC way on purpose (decorators), to compare with the
 * signal-based input()/output() used elsewhere:
 *   @Input()  -> data flows DOWN from the parent:   <app-pager [page]="p" [totalPages]="t">
 *   @Output() -> events flow UP to the parent:       (pageChange)="onPage($event)"
 *   ngOnChanges runs whenever an @Input reference changes (before ngOnInit the first time).
 */
@Component({
  selector: 'app-pager',
  template: `
    @if (totalPages > 1) {
      <nav class="pager" aria-label="Pagination">
        <button type="button" class="btn btn-small" [disabled]="page <= 1" (click)="go(page - 1)">‹ Prev</button>
        @for (p of pages; track p) {
          @if (p === 0) {
            <span class="pager-gap">…</span>
          } @else {
            <button type="button" class="btn btn-small" [class.active]="p === page"
                    [attr.aria-current]="p === page ? 'page' : null" (click)="go(p)">{{ p }}</button>
          }
        }
        <button type="button" class="btn btn-small" [disabled]="page >= totalPages" (click)="go(page + 1)">Next ›</button>
        <span class="muted small">{{ totalCount }} records</span>
      </nav>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PagerComponent implements OnChanges {
  @Input({ required: true }) page = 1;
  @Input({ required: true }) totalPages = 0;
  @Input() totalCount = 0;
  @Output() readonly pageChange = new EventEmitter<number>();

  pages: number[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['page'] || changes['totalPages']) this.pages = this.buildPages();
  }

  go(p: number): void {
    if (p >= 1 && p <= this.totalPages && p !== this.page) this.pageChange.emit(p);
  }

  /** 1 … 4 5 [6] 7 8 … 20  (0 = gap) */
  private buildPages(): number[] {
    const set = new Set([1, this.totalPages, this.page - 2, this.page - 1, this.page, this.page + 1, this.page + 2]);
    const sorted = [...set].filter((p) => p >= 1 && p <= this.totalPages).sort((a, b) => a - b);
    return sorted.flatMap((p, i) => (i > 0 && p - sorted[i - 1] > 1 ? [0, p] : [p]));
  }
}
