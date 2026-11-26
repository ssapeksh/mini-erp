import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink],
  template: `
    <section class="empty-state">
      <h1>Page not found</h1>
      <p class="muted">The address doesn't match any page.</p>
      <a class="btn btn-primary" routerLink="/dashboard">Go to dashboard</a>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundPageComponent {}

@Component({
  selector: 'app-forbidden-page',
  imports: [RouterLink],
  template: `
    <section class="empty-state">
      <h1>Not allowed</h1>
      <p class="muted">Your role doesn't include the permission for that page.</p>
      <a class="btn btn-primary" routerLink="/dashboard">Go to dashboard</a>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForbiddenPageComponent {}
