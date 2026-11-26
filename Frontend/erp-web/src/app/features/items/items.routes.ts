import { inject } from '@angular/core';
import { RedirectCommand, ResolveFn, Router, Routes } from '@angular/router';
import { catchError, of } from 'rxjs';
import { Item } from '../../core/api/api.models';
import { permissionGuard, unsavedChangesGuard } from '../../core/auth/auth.guards';
import { Permissions } from '../../core/auth/permissions';
import { ItemsService } from './items.service';

/**
 * RESOLVER: loads the item BEFORE the edit page opens, so the component never
 * renders half-empty. withComponentInputBinding() hands the result to the
 * component's `item` input. Unknown id -> redirect back to the list.
 */
export const itemResolver: ResolveFn<Item | RedirectCommand> = (route) => {
  const router = inject(Router);
  return inject(ItemsService)
    .get(Number(route.paramMap.get('id')))
    .pipe(catchError(() => of(new RedirectCommand(router.parseUrl('/items')))));
};

/** Child routes, lazy-loaded as one chunk via loadChildren in app.routes.ts. */
export const ITEMS_ROUTES: Routes = [
  {
    path: '',
    title: 'Items · MiniErp',
    loadComponent: () => import('./item-list.component').then((m) => m.ItemListComponent),
  },
  {
    path: 'new',
    title: 'New item · MiniErp',
    canActivate: [permissionGuard],
    canDeactivate: [unsavedChangesGuard],
    data: { permission: Permissions.ItemsCreate },
    loadComponent: () => import('./item-form.component').then((m) => m.ItemFormComponent),
  },
  {
    path: ':id/edit',
    title: 'Edit item · MiniErp',
    canActivate: [permissionGuard],
    canDeactivate: [unsavedChangesGuard],
    data: { permission: Permissions.ItemsEdit },
    resolve: { item: itemResolver },
    loadComponent: () => import('./item-form.component').then((m) => m.ItemFormComponent),
  },
];
