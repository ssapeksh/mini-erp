import { AsyncPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { Permissions } from '../core/auth/permissions';
import { CurrentUserStore } from '../core/state/current-user.store';
import { LoadingService } from '../core/ui/loading.service';
import { HasPermissionDirective } from '../shared/directives/has-permission.directive';

/**
 * Layout for every logged-in page: header + nav + <router-outlet> where the
 * child route's component is rendered.
 * - routerLink: navigation without a full page reload; routerLinkActive adds a CSS class
 * - *appHasPermission: menu items only for users who may open them
 * - the user name comes from the BehaviorSubject store via the async pipe;
 *   the progress bar and initials come from signals (both styles side by side)
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, AsyncPipe, HasPermissionDirective],
  template: `
    @if (loading.isLoading()) { <div class="progress" role="progressbar" aria-label="Loading"></div> }

    <header class="topbar">
      <a class="brand" routerLink="/dashboard">Mini<span>Erp</span></a>

      <nav class="nav" aria-label="Main">
        <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
        <a *appHasPermission="P.ItemsView" routerLink="/items" routerLinkActive="active">Items</a>
        <a *appHasPermission="P.OrdersView" routerLink="/orders" routerLinkActive="active">Orders</a>
        <a *appHasPermission="P.StockView" routerLink="/stock" routerLinkActive="active">Stock</a>
        <a *appHasPermission="P.UsersManage" routerLink="/admin/sessions" routerLinkActive="active">Sessions</a>
      </nav>

      <div class="user">
        <span class="avatar" aria-hidden="true">{{ auth.initials() }}</span>
        <span class="user-name">{{ userStore.displayName$ | async }}</span>
        <button type="button" class="btn btn-small" (click)="auth.logout()">Sign out</button>
      </div>
    </header>

    <main class="page">
      <router-outlet />
    </main>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly userStore = inject(CurrentUserStore);
  protected readonly loading = inject(LoadingService);
  protected readonly P = Permissions;
}
