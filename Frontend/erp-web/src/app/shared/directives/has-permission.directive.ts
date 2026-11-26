import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';

/**
 * CUSTOM STRUCTURAL DIRECTIVE (like *ngIf, it adds/removes DOM):
 *   <button *appHasPermission="'Items.Create'">New item</button>
 *   <div *appHasPermission="['Orders.Approve.L1', 'Orders.Approve.L2']">...</div>
 * The * means Angular wraps the element in an <ng-template>; we create the
 * view from that template only when the user has one of the permissions.
 * The effect() re-runs if the permissions signal changes (login/logout).
 * Hiding a button is only UX: the API still checks the permission (403).
 */
@Directive({ selector: '[appHasPermission]' })
export class HasPermissionDirective {
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);
  private readonly auth = inject(AuthService);
  private shown = false;

  readonly appHasPermission = input.required<string | string[]>();

  constructor() {
    effect(() => {
      const needed = this.appHasPermission();
      const codes = Array.isArray(needed) ? needed : [needed];
      const allowed = this.auth.hasAnyPermission(...codes);
      if (allowed && !this.shown) {
        this.container.createEmbeddedView(this.template);
        this.shown = true;
      } else if (!allowed && this.shown) {
        this.container.clear();
        this.shown = false;
      }
    });
  }
}
