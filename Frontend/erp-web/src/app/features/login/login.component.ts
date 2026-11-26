import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { messageOf } from '../../core/http/error.interceptor';
import { AutofocusDirective } from '../../shared/directives/autofocus.directive';

/**
 * TEMPLATE-DRIVEN FORM (FormsModule): the form model lives in the HTML.
 *   [(ngModel)]="userName" -> two-way binding (property binding + event binding)
 *   #form="ngForm"         -> template reference to the NgForm directive (valid, submitted...)
 *   #user="ngModel"        -> state of one control (touched, errors)
 * Good for small simple forms. Compare with the REACTIVE item/order forms,
 * where the model lives in the TypeScript class.
 */
@Component({
  selector: 'app-login',
  imports: [FormsModule, AutofocusDirective],
  template: `
    <div class="login-wrap">
      <form class="card login-card" #form="ngForm" (ngSubmit)="submit(form.valid)" novalidate>
        <h1 class="brand big">Mini<span>Erp</span></h1>
        <p class="muted">Sign in to continue</p>

        @if (reason() === 'session') {
          <p class="alert">Your session ended (signed in elsewhere, signed out, idle or revoked). Please sign in again.</p>
        } @else if (reason() === 'expired') {
          <p class="alert">Your session expired. Please sign in again.</p>
        }

        <label for="userName">User name</label>
        <input id="userName" name="userName" [(ngModel)]="userName" #user="ngModel"
               required autocomplete="username" appAutofocus />
        @if (user.invalid && (user.touched || form.submitted)) {
          <small class="field-error">User name is required.</small>
        }

        <label for="password">Password</label>
        <input id="password" name="password" type="password" [(ngModel)]="password" #pwd="ngModel"
               required minlength="4" autocomplete="current-password" />
        @if (pwd.errors?.['required'] && (pwd.touched || form.submitted)) {
          <small class="field-error">Password is required.</small>
        } @else if (pwd.errors?.['minlength'] && pwd.touched) {
          <small class="field-error">At least 4 characters.</small>
        }

        @if (error()) { <p class="alert" role="alert">{{ error() }}</p> }

        <button type="submit" class="btn btn-primary btn-block" [disabled]="busy()">
          {{ busy() ? 'Signing in…' : 'Sign in' }}
        </button>

        <details class="demo-users">
          <summary>Demo users (password Demo&#64;123)</summary>
          <ul>
            @for (u of demoUsers; track u.name) {
              <li><button type="button" class="btn-link" (click)="userName = u.name; password = 'Demo@123'">{{ u.name }}</button> {{ u.role }}</li>
            }
          </ul>
        </details>
      </form>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Query params arrive as inputs thanks to withComponentInputBinding(). */
  readonly returnUrl = input<string>();
  readonly reason = input<string>();

  userName = '';
  password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly demoUsers = [
    { name: 'admin', role: 'everything' },
    { name: 'manager1', role: 'level-2 approver' },
    { name: 'approver1', role: 'level-1 approver' },
    { name: 'store1', role: 'creates & despatches orders' },
    { name: 'viewer1', role: 'read only' },
  ];

  submit(valid: boolean | null): void {
    if (!valid || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);

    this.auth.login(this.userName.trim(), this.password).subscribe({
      next: () => void this.router.navigateByUrl(this.safeReturnUrl()),
      error: (e: HttpErrorResponse) => {
        this.error.set(messageOf(e));
        this.busy.set(false);
      },
    });
  }

  /** Only allow in-app paths, so ?returnUrl=https://evil.example can't redirect (open redirect). */
  private safeReturnUrl(): string {
    const url = this.returnUrl();
    return url && url.startsWith('/') && !url.startsWith('//') ? url : '/dashboard';
  }
}
