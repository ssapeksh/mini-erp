import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withHooks, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, concatMap, exhaustMap, from, pipe, switchMap, tap, toArray } from 'rxjs';

import { OrderQuery, OrderSummary, PagedResult } from '../../core/api/api.models';
import { ToastService } from '../../core/ui/toast.service';
import { OrdersService } from './orders.service';

interface OrderListState {
  query: OrderQuery;
  result: PagedResult<OrderSummary> | null;
  loading: boolean;
  selected: number[];
}

/**
 * NgRx SIGNAL STORE: state + derived state + methods in one place.
 *   withState    -> the state (each key becomes a signal)
 *   withComputed -> derived values (like selectors)
 *   withMethods  -> the only way to change state (patchState), plus side effects
 *   rxMethod     -> an RxJS-powered method (like an NgRx Effect): here it reloads
 *                   whenever the `query` signal changes, cancelling the previous call
 *   withHooks    -> lifecycle (onInit)
 * Classic NgRx (Store + Actions + Reducers + Effects + Selectors) solves the
 * same problem with more ceremony for big apps with lots of shared state.
 * Provided in the COMPONENT's providers: each list page gets a fresh store.
 */
export const OrderListStore = signalStore(
  withState<OrderListState>({
    query: { status: '', search: '', page: 1, pageSize: 15 },
    result: null,
    loading: false,
    selected: [],
  }),
  withComputed(({ result, selected }) => ({
    orders: computed(() => result()?.items ?? []),
    selectedCount: computed(() => selected().length),
    selectedDrafts: computed(() =>
      (result()?.items ?? []).filter((o) => o.status === 'Draft' && selected().includes(o.id))),
  })),
  withMethods((store, api = inject(OrdersService), toast = inject(ToastService)) => ({
    setFilter(patch: Partial<OrderQuery>): void {
      patchState(store, (s) => ({ query: { ...s.query, ...patch, page: patch.page ?? 1 }, selected: [] }));
    },
    toggle(id: number): void {
      patchState(store, (s) => ({
        selected: s.selected.includes(id) ? s.selected.filter((x) => x !== id) : [...s.selected, id],
      }));
    },
    clearSelection(): void {
      patchState(store, { selected: [] });
    },

    /** switchMap: a new query cancels the in-flight one. */
    load: rxMethod<OrderQuery>(
      pipe(
        tap(() => patchState(store, { loading: true })),
        switchMap((q) => api.list(q).pipe(
          tap((result) => patchState(store, { result, loading: false })),
          catchError(() => {
            patchState(store, { loading: false });
            return EMPTY;
          }),
        )),
      ),
    ),

    /**
     * Submit every selected draft ONE AT A TIME (concatMap keeps order and
     * never runs two at once; mergeMap would fire them all in parallel).
     * exhaustMap on the outside ignores extra clicks while a batch is running.
     */
    submitSelected: rxMethod<void>(
      pipe(
        exhaustMap(() => {
          const drafts = store.selectedDrafts();
          if (drafts.length === 0) {
            toast.info('Select one or more Draft orders first.');
            return EMPTY;
          }
          return from(drafts).pipe(
            concatMap((o) => api.submit(o.id).pipe(catchError(() => EMPTY))),
            toArray(),
            tap((done) => {
              toast.success(`${done.length} of ${drafts.length} order(s) submitted for approval.`);
              patchState(store, (s) => ({ selected: [], query: { ...s.query } }));   // new object -> load() re-runs
            }),
          );
        }),
      ),
    ),
  })),
  withHooks({
    onInit(store) {
      store.load(store.query);        // passing the SIGNAL: load re-runs every time the query changes
    },
  }),
);
