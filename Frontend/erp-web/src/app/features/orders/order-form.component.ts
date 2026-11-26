import { AsyncPipe, CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable, concatMap, map, of, startWith } from 'rxjs';

import { CreateOrderRequest, ItemListItem } from '../../core/api/api.models';
import { LookupsService } from '../../core/api/lookups.service';
import { HasUnsavedChanges } from '../../core/auth/auth.guards';
import { ToastService } from '../../core/ui/toast.service';
import { CardComponent } from '../../shared/components/card.component';
import { uniqueItemsValidator } from '../../shared/validators/erp-validators';
import { ItemsService } from '../items/items.service';
import { OrdersService } from './orders.service';

type LineForm = FormGroup<{ itemId: import('@angular/forms').FormControl<number>; qty: import('@angular/forms').FormControl<number> }>;

/**
 * New order with a DYNAMIC list of lines: a FormArray of FormGroups. Lines are
 * added/removed at run time; the array has its own validators (at least one
 * line, no duplicate items).
 */
@Component({
  selector: 'app-order-form',
  imports: [ReactiveFormsModule, RouterLink, AsyncPipe, CurrencyPipe, CardComponent],
  templateUrl: './order-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderFormComponent implements HasUnsavedChanges {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly orders = inject(OrdersService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly lookups = inject(LookupsService);

  protected readonly warehouses$ = this.lookups.warehouses$;
  protected readonly customers = toSignal(this.lookups.customers('', 1, 100).pipe(map((p) => p.items)), { initialValue: [] });
  protected readonly items = toSignal(
    inject(ItemsService).list({ sortBy: 'Code', sortDir: 'ASC', page: 1, pageSize: 100 }).pipe(map((p) => p.items.filter((i) => i.isActive))),
    { initialValue: [] as ItemListItem[] },
  );

  protected readonly saving = signal(false);
  private saved = false;
  /** One key per submission attempt; reset when the form changes (so a NEW order gets a NEW key). */
  private idempotencyKey: string | null = null;

  protected readonly form = this.fb.group({
    customerId: [0, [Validators.min(1)]],
    warehouseId: [1, [Validators.min(1)]],
    remarks: ['', [Validators.maxLength(500)]],
    lines: this.fb.array<LineForm>([this.newLine()], [Validators.required, Validators.minLength(1), uniqueItemsValidator]),
  });

  get lines(): FormArray<LineForm> {
    return this.form.controls.lines;
  }

  /** Live estimate: form value (as a signal) + item prices. The server recalculates the real totals and discounts. */
  private readonly formValue = toSignal(this.form.valueChanges.pipe(startWith(this.form.getRawValue())));
  protected readonly estimate = computed(() => {
    const prices = new Map(this.items().map((i) => [i.id, i.unitPrice] as const));
    return (this.formValue()?.lines ?? []).reduce((sum, l) => sum + (prices.get(l?.itemId ?? 0) ?? 0) * (l?.qty ?? 0), 0);
  });

  constructor() {
    this.form.valueChanges.subscribe(() => (this.idempotencyKey = null));
  }

  addLine(): void {
    this.lines.push(this.newLine());
  }

  removeLine(index: number): void {
    if (this.lines.length > 1) this.lines.removeAt(index);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty && !this.saved;
  }

  /**
   * submitAfterSave = true: create, THEN submit. concatMap runs the second
   * request only after the first completes, and passes its result (the new id).
   */
  save(submitAfterSave: boolean): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.idempotencyKey ??= crypto.randomUUID();     // same key if the user retries the same order

    const v = this.form.getRawValue();
    const body: CreateOrderRequest = {
      customerId: v.customerId,
      warehouseId: v.warehouseId,
      remarks: v.remarks || null,
      lines: v.lines.map((l) => ({ itemId: l.itemId, qty: l.qty })),
    };

    this.orders.create(body, this.idempotencyKey).pipe(
      concatMap((order): Observable<{ id: number; orderNo: string; status: string }> =>
        submitAfterSave ? this.orders.submit(order.id) : of(order)),
    ).subscribe({
      next: (order) => {
        this.saved = true;
        this.toast.success(`Order ${order.orderNo} saved (${order.status}).`);
        void this.router.navigate(['/orders', order.id]);
      },
      error: () => this.saving.set(false),
    });
  }

  itemLabel(id: number): string {
    const i = this.items().find((x) => x.id === id);
    return i ? `${i.unitPrice} / ${i.unit}` : '';
  }

  private newLine(): LineForm {
    return this.fb.group({
      itemId: [0, [Validators.min(1)]],
      qty: [1, [Validators.required, Validators.min(0.001)]],
    });
  }
}
