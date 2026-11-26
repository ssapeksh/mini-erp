import { Injectable } from '@angular/core';
import { BehaviorSubject, distinctUntilChanged, map } from 'rxjs';
import { UserInfo } from '../auth/auth.models';

/**
 * Shared state the CLASSIC RxJS way, kept next to the signal-based AuthService
 * on purpose so you can compare them in an interview.
 *
 * Subject vs BehaviorSubject:
 *   - Subject: no current value; late subscribers miss earlier emissions.
 *   - BehaviorSubject: holds the CURRENT value and gives it to every new
 *     subscriber immediately, which is what "the logged-in user" needs.
 *
 * The subject is private; components get a read-only Observable (encapsulation),
 * and read it in templates with the async pipe (which also unsubscribes).
 * AuthService keeps it in sync with an effect().
 */
@Injectable({ providedIn: 'root' })        // one app-wide SINGLETON (root injector)
export class CurrentUserStore {
  private readonly user$$ = new BehaviorSubject<UserInfo | null>(null);

  readonly user$ = this.user$$.asObservable();
  readonly displayName$ = this.user$.pipe(
    map((u) => (u ? `${u.fullName} (${u.role})` : '')),
    distinctUntilChanged(),
  );

  /** Synchronous snapshot, a BehaviorSubject feature. */
  get snapshot(): UserInfo | null {
    return this.user$$.value;
  }

  set(user: UserInfo | null): void {
    this.user$$.next(user);
  }
}
