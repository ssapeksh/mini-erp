import { Routes } from '@angular/router';
import { permissionGuard, unsavedChangesGuard } from '../../core/auth/auth.guards';
import { Permissions } from '../../core/auth/permissions';

export const ORDERS_ROUTES: Routes = [
  {
    path: '',
    title: 'Orders · MiniErp',
    loadComponent: () => import('./order-list.component').then((m) => m.OrderListComponent),
  },
  {
    path: 'new',
    title: 'New order · MiniErp',
    canActivate: [permissionGuard],
    canDeactivate: [unsavedChangesGuard],
    data: { permission: Permissions.OrdersCreate },
    loadComponent: () => import('./order-form.component').then((m) => m.OrderFormComponent),
  },
  {
    path: ':id',                      // route param -> `id` input of the detail component
    title: 'Order · MiniErp',
    loadComponent: () => import('./order-detail.component').then((m) => m.OrderDetailComponent),
  },
];
