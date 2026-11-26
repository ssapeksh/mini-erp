import { AsyncPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnInit, ViewChild, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { Item, ItemSaveRequest } from '../../core/api/api.models';
import { LookupsService } from '../../core/api/lookups.service';
import { HasUnsavedChanges } from '../../core/auth/auth.guards';
import { ToastService } from '../../core/ui/toast.service';
import { CardComponent } from '../../shared/components/card.component';
import { codeAvailableValidator, gstSlabValidator } from '../../shared/validators/erp-validators';
import { ItemsService } from './items.service';

/**
 * REACTIVE FORM (ReactiveFormsModule): the model lives in the class as a
 * typed FormGroup; the template only binds to it. Synchronous, easy to unit
 * test, and good for complex/dynamic forms (compare the template-driven login).
 * Used for both create (/items/new) and edit (/items/:id/edit, data from the resolver).
 */
@Component({
  selector: 'app-item-form',
  imports: [ReactiveFormsModule, RouterLink, AsyncPipe, CardComponent],
  templateUrl: './item-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ItemFormComponent implements OnInit, AfterViewInit, HasUnsavedChanges {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly itemsApi = inject(ItemsService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly categories$ = inject(LookupsService).categories$;

  /** Set by the resolver on the edit route (withComponentInputBinding). */
  readonly item = input<Item>();

  /** @ViewChild: a reference to an element in THIS component's own template. */
  @ViewChild('nameInput') private nameInput?: ElementRef<HTMLInputElement>;

  protected readonly saving = signal(false);
  protected readonly conflict = signal(false);
  private saved = false;

  protected readonly form = this.fb.group({
    code: ['', {
      validators: [Validators.required, Validators.pattern(/^[A-Z]{3}-\d{3}$/)],
      asyncValidators: [codeAvailableValidator((code) => this.itemsApi.isCodeAvailable(code, this.item()?.id))],
      updateOn: 'change',
    }],
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    categoryId: [0, [Validators.required, Validators.min(1)]],
    unit: ['NOS', [Validators.required, Validators.maxLength(10)]],
    unitPrice: [0, [Validators.required, Validators.min(0)]],
    taxPercent: [18, [Validators.required, gstSlabValidator]],
    reorderLevel: [0, [Validators.required, Validators.min(0)]],
    isActive: [true],
  });

  protected get isEdit(): boolean {
    return !!this.item();
  }

  /** ngOnInit: inputs are set by now (unlike in the constructor), so fill the form here. */
  ngOnInit(): void {
    const item = this.item();
    if (item) {
      this.form.patchValue(item);
      this.form.controls.code.disable();            // the code is the business key: not editable
    }
  }

  /** ngAfterViewInit: the view (and #nameInput) exists now. */
  ngAfterViewInit(): void {
    if (this.isEdit) queueMicrotask(() => this.nameInput?.nativeElement.focus());
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty && !this.saved;
  }

  upperCaseCode(): void {
    const c = this.form.controls.code;
    c.setValue(c.value.toUpperCase(), { emitEvent: true });
  }

  save(): void {
    if (this.form.invalid || this.form.pending) {
      this.form.markAllAsTouched();                 // reveal every error message
      return;
    }
    this.saving.set(true);
    this.conflict.set(false);
    const body = this.form.getRawValue() as ItemSaveRequest;   // getRawValue includes the disabled code
    const item = this.item();
    const request$ = item ? this.itemsApi.update(item.id, body, item.rowVersion) : this.itemsApi.create(body);

    request$.subscribe({
      next: (saved) => {
        this.saved = true;
        this.toast.success(`${saved.code} saved.`);
        void this.router.navigate(['/items']);
      },
      error: (e: HttpErrorResponse) => {
        this.saving.set(false);
        if (e.status === 409 && item) this.conflict.set(true);   // someone else saved first
      },
    });
  }

  reload(): void {
    const item = this.item();
    if (!item) return;
    this.saved = true;          // allow the navigation without the unsaved-changes prompt
    void this.router.navigateByUrl('/items', { skipLocationChange: true })
      .then(() => this.router.navigate(['/items', item.id, 'edit']));
  }
}
