import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from './notification.service';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notifications = inject(NotificationService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      const message = err.error?.error ?? err.error?.message ?? err.message ?? 'An unexpected error occurred';
      notifications.error(message);
      return throwError(() => err);
    })
  );
};
