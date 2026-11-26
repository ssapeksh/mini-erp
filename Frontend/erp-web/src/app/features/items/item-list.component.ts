import { AsyncPipe, CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, catchError, combineLatest, debounceTime, distinctUntilChanged, map, of, startWith, switchMap, tap } from 'rxjs';

import { ItemHistory, ItemListItem, ItemQuery, PagedResult } from '../../core/api/api.models';
import { LookupsService } from '../../core/api/lookups.service';
import { Permissions } from '../../core/auth/permissions';
import { ToastService } from '../../core/ui/toast.service';
import { CardComponent } from '../../shared/components/card.component';
import { PagerComponent } from '../../shared/components/pager.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge.component';
import { HasPermissionDirective } from '../../shared/directives/has-permission.directive';
import { StockStatusPipe } from '../../shared/pipes/stock-status.pipe';
import { ItemsService } from './items.service';

/**
 * Item list. The whole screen is ONE reactive pipeline:
 *
 *   search box ──debounceTime(300)──distinctUntilChanged──┐
 *   category, sort, page (signals -> toObservable) ───────┼─ combineLatest ─ switchMap(HTTP) ─ toSignal ─ template
 *   reload$ (after delete) ───────────────────────────────┘
 *
 * switchMap CANCELS the previous request when a new query arrives, so a slow
 * response for "le" can never overwrite the newer result for "led".
 */
@Component({
  selector: 'app-item-list',
  imports: [ReactiveFormsModule, RouterLink, AsyncPipe, CurrencyPipe, DecimalPipe, DatePipe,
            CardComponent, PagerComponent, StatusBadgeComponent, HasPermissionDirective, StockStatusPipe],
  templateUrl: './item-list.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,   // re-render only when inputs/signals/events change
})
export class ItemListComponent {
  private readonly items = inject(ItemsService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly categories$ = inject(LookupsService).categories$;
  protected readonly P = Permissions;

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly category = new FormControl<number | null>(null);
  protected readonly sort = signal<{ by: ItemQuery['sortBy']; dir: ItemQuery['sortDir'] }>({ by: 'Name', dir: 'ASC' });
  protected readonly page = signal(1);
  private readonly reload$ = new Subject<void>();

  protected readonly history = signal<{ item: ItemListItem; rows: ItemHistory[] } | null>(null);

  private readonly empty: PagedResult<ItemListItem> =
    { items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0, hasNext: false, hasPrevious: false };

  protected readonly result = toSignal(
    combineLatest([
      this.search.valueChanges.pipe(
        startWith(''),
        debounceTime(300),                                   // wait until typing pauses
        map((s) => s.trim()),
        distinctUntilChanged(),                              // ignore "led" -> "led " -> "led"
        tap(() => this.page.set(1)),                         // new search starts at page 1
      ),
      this.category.valueChanges.pipe(startWith(null), tap(() => this.page.set(1))),
      toObservable(this.sort),
      toObservable(this.page),
      this.reload$.pipe(startWith(undefined)),
    ]).pipe(
      debounceTime(0),                                       // coalesce the page reset + filter change into one call
      map(([search, categoryId, sort, page]): ItemQuery =>
        ({ search, categoryId, sortBy: sort.by, sortDir: sort.dir, page, pageSize: 10 })),
      switchMap((q) => this.items.list(q).pipe(catchError(() => of(this.empty)))),
    ),
    { initialValue: null },
  );

  toggleSort(by: ItemQuery['sortBy']): void {
    this.sort.update((s) => ({ by, dir: s.by === by && s.dir === 'ASC' ? 'DESC' : 'ASC' }));
  }

  sortIcon(by: ItemQuery['sortBy']): string {
    const s = this.sort();
    return s.by !== by ? '' : s.dir === 'ASC' ? '▲' : '▼';
  }

  remove(item: ItemListItem): void {
    if (!confirm(`Delete ${item.code} - ${item.name}?`)) return;
    this.items.delete(item.id)
      .pipe(takeUntilDestroyed(this.destroyRef))            // cancel if the user navigates away
      .subscribe(() => {
        this.toast.success(`${item.code} deleted.`);
        this.reload$.next();
      });
  }

  showHistory(item: ItemListItem): void {
    this.items.history(item.id).subscribe((rows) => this.history.set({ item, rows }));
  }

  exportCsv(): void {
    this.items.exportCsv().subscribe((blob) => {
      const url = URL.createObjectURL(blob);
      const a = Object.assign(document.createElement('a'), { href: url, download: 'items.csv' });
      a.click();
      URL.revokeObjectURL(url);                             // free the memory
    });
  }

}
