import { Pipe, PipeTransform } from '@angular/core';

/**
 * CUSTOM PIPE: {{ item.stockQty | stockStatus: item.reorderLevel }} -> 'Reorder' | 'Low' | 'OK'
 * PURE (the default): Angular re-runs transform() only when an input value or
 * reference changes, so it's as cheap as a cached function. An impure pipe
 * (pure: false) runs on EVERY change detection, which is slow; avoid it.
 * Prefer a pure pipe over calling a method in a template, which also runs every time.
 */
@Pipe({ name: 'stockStatus' })
export class StockStatusPipe implements PipeTransform {
  transform(quantity: number | null | undefined, reorderLevel: number): 'Reorder' | 'Low' | 'OK' {
    const qty = quantity ?? 0;
    if (qty <= reorderLevel) return 'Reorder';
    if (qty <= reorderLevel * 1.5) return 'Low';
    return 'OK';
  }
}
