import { Routes } from '@angular/router';
import { authGuard, permissionGuard } from './core/auth/auth.guards';
import { Permissions } from './core/auth/permissions';
import { ShellComponent } from './layout/shell.component';

/**
 * ROUTING
 * - LAZY LOADING: loadComponent / loadChildren download a feature's code
 *   only when its URL is first visited -> smaller initial bundle.
 * - GUARDS: authGuard on the shell (must be logged in), permissionGuard per
 *   feature (route data says which permission).
 * - title: the browser tab title for each route.
 */
export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · MiniErp',
    loadComponent: () => import('./features/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    component: ShellComponent,                 // layout with nav, loaded eagerly
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Dashboard · MiniErp',
        loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'items',
        canActivate: [permissionGuard],
        data: { permission: Permissions.ItemsView },
        loadChildren: () => import('./features/items/items.routes').then((m) => m.ITEMS_ROUTES),
      },
      {
        path: 'orders',
        canActivate: [permissionGuard],
        data: { permission: Permissions.OrdersView },
        loadChildren: () => import('./features/orders/orders.routes').then((m) => m.ORDERS_ROUTES),
      },
      {
        path: 'stock',
        title: 'Stock · MiniErp',
        canActivate: [permissionGuard],
        data: { permission: Permissions.StockView },
        loadComponent: () => import('./features/stock/stock.component').then((m) => m.StockComponent),
      },
      {
        path: 'admin/sessions',
        title: 'Sessions · MiniErp',
        canActivate: [permissionGuard],
        data: { permission: Permissions.UsersManage },
        loadComponent: () => import('./features/admin/sessions.component').then((m) => m.SessionsComponent),
      },
      {
        path: 'forbidden',
        title: 'Not allowed · MiniErp',
        loadComponent: () => import('./shared/pages/status-pages').then((m) => m.ForbiddenPageComponent),
      },
      {
        path: '**',
        title: 'Not found · MiniErp',
        loadComponent: () => import('./shared/pages/status-pages').then((m) => m.NotFoundPageComponent),
      },
    ],
  },
];
