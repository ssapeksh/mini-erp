# erp-web: MiniErp Angular client

Angular 21 single-page app for MiniErp: standalone components, zoneless change detection, signals, RxJS, reactive and template-driven forms, an NgRx SignalStore, functional interceptors and guards.

## Run

```bash
# 1. the API must be running on http://localhost:5101 (see the repo README)
# 2. then:
cd Frontend/erp-web
npm install
npm start            # ng serve -> http://localhost:4200
```

Sign in with any demo user (password `Demo@123`): `admin`, `manager1`, `approver1`, `store1`, `viewer1`.
Each one sees different menus and buttons because the UI follows the user's permissions.

```bash
npm run build        # production build -> dist/erp-web (lazy chunks per feature)
npm test             # unit tests (Vitest), added in Phase 4
```

The API address is in `src/environments/environment.ts`. The API allows CORS from `http://localhost:4200`.

## Structure

```
src/app/
  app.config.ts         providers: router, HttpClient + interceptors, zoneless, locale, app initializer
  app.routes.ts         lazy routes + guards
  core/
    api/                models (ApiResponse<T>, DTOs), lookups/reports services, injection tokens
    auth/               AuthService (signals), guards, permission codes
    http/               auth, error (refresh + retry) and loading interceptors
    state/              CurrentUserStore (BehaviorSubject)
    ui/                 toast + loading services
  layout/               shell (nav, user menu, router outlet)
  shared/               card (content projection), pager (@Input/@Output), status badge (input()),
                        *appHasPermission (structural directive), appAutofocus, stockStatus pipe, validators
  features/
    login/              template-driven form
    dashboard/          toSignal + computed + @defer
    items/              list (debounce + switchMap pipeline), reactive form with sync + async validators, resolver
    orders/             NgRx SignalStore list, FormArray order form, detail with exhaustMap workflow buttons
    stock/              classic async-pipe page, ngOnInit/ngOnDestroy timer
    admin/              active sessions (Session Insight)
```

## Flow to try

1. `store1` → **Orders → New order** → add two lines → *Save & submit*.
2. `approver1` → open the order → *Approve level 1*.
3. `manager1` → *Approve level 2* (the dashboard shows KPIs for this role).
4. `store1` → *Despatch* (double-clicking still sends one request).
5. Sign in as `store1` in a second browser: the first one is sent back to the login page (single session).
