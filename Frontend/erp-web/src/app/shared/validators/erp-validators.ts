import { AbstractControl, AsyncValidatorFn, FormArray, ValidationErrors, ValidatorFn } from '@angular/forms';
import { Observable, catchError, map, of, switchMap, timer } from 'rxjs';

/*  CUSTOM VALIDATORS: plain functions returning null (valid) or an error object.  */

const GST_SLABS = [0, 5, 12, 18, 28];

/** Sync validator: value must be an Indian GST slab (mirrors the API's FluentValidation rule). */
export const gstSlabValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  control.value === null || control.value === '' || GST_SLABS.includes(Number(control.value))
    ? null
    : { gstSlab: { allowed: GST_SLABS } };

/**
 * ASYNC validator factory: asks the API whether an item code is free.
 * timer(400) + switchMap = debounce: Angular re-runs async validators on every
 * keystroke and unsubscribes from the previous run, so only the last value
 * (after 400 ms of no typing) actually calls the server.
 * While it runs, control.status is 'PENDING'.
 */
export function codeAvailableValidator(
  isAvailable: (code: string) => Observable<boolean>,
): AsyncValidatorFn {
  return (control: AbstractControl): Observable<ValidationErrors | null> => {
    const code = String(control.value ?? '').trim().toUpperCase();
    if (!/^[A-Z]{3}-\d{3}$/.test(code)) return of(null);       // format is the sync validator's job
    return timer(400).pipe(
      switchMap(() => isAvailable(code)),
      map((available) => (available ? null : { codeTaken: true })),
      catchError(() => of(null)),                              // can't check -> don't block the user
    );
  };
}

/** FormArray-level validator: the same item may not appear on two order lines. */
export const uniqueItemsValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const ids = (control as FormArray).controls
    .map((line) => line.get('itemId')?.value as number | null)
    .filter((id): id is number => !!id);                       // type guard narrows (number|null)[] to number[]
  return new Set(ids).size === ids.length ? null : { duplicateItems: true };
};
