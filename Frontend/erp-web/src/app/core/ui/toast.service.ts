import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  kind: 'success' | 'error' | 'info';
  text: string;
}

/** App-wide notifications. A signal holds the list; ToastContainerComponent renders it. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  private readonly _toasts = signal<Toast[]>([]);
  readonly toasts = this._toasts.asReadonly();

  success(text: string): void { this.show('success', text); }
  error(text: string): void { this.show('error', text, 6000); }
  info(text: string): void { this.show('info', text); }

  dismiss(id: number): void {
    // immutable update: a NEW array, so the signal notifies its readers
    this._toasts.update((list) => list.filter((t) => t.id !== id));
  }

  private show(kind: Toast['kind'], text: string, ms = 3500): void {
    const toast: Toast = { id: this.nextId++, kind, text };
    this._toasts.update((list) => [...list.slice(-3), toast]);   // keep at most 4
    setTimeout(() => this.dismiss(toast.id), ms);
  }
}
