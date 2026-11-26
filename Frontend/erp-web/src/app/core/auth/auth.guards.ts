import { inject } from '@angular/core';
import { CanActivateFn, CanDeactivateFn, Router } from '@angular/router';
import { ToastService } from '../ui/toast.service';
import { AuthService } from './auth.service';

/*  FUNCTIONAL ROUTE GUARDS (the modern replacement for guard classes).
    Return true to allow, or a UrlTree to redirect.                         */

/** canActivate: only logged-in users; remembers where they wanted to go. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isLoggedIn() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** canActivate: route data { permission: 'Items.View' } must be one of the user's permissions. */
export const permissionGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const needed = route.data['permission'] as string | undefined;
  if (!needed || auth.hasPermission(needed)) return true;

  inject(ToastService).error(`You need the "${needed}" permission to open that page.`);
  return inject(Router).createUrlTree(['/forbidden']);
};

/** A component that can tell the guard whether it has unsaved edits. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/** canDeactivate: ask before leaving a form with unsaved changes. */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) =>
  !component.hasUnsavedChanges() || confirm('You have unsaved changes. Leave this page and lose them?');
