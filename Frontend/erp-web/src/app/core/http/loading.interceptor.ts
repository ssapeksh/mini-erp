import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { LoadingService } from '../ui/loading.service';

/** Global busy indicator: +1 when a request starts, -1 when it completes, errors or is cancelled. */
export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  const loading = inject(LoadingService);
  loading.start();
  return next(req).pipe(finalize(() => loading.stop()));   // finalize runs on complete, error AND unsubscribe
};
