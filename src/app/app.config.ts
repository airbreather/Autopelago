import { provideHttpClient } from '@angular/common/http';
import {
  type ApplicationConfig,
  ErrorHandler,
  isDevMode,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';

import { provideRouter, withComponentInputBinding, withHashLocation } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';

import { provideToastr, ToastNoAnimation } from 'ngx-toastr';
import { AppErrorHandler } from './app-error-handler';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideToastr({
      positionClass: 'toast-top-full-width',
      closeButton: true,
      progressBar: true,
      toastComponent: ToastNoAnimation,
    }),
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withHashLocation(), withComponentInputBinding()),
    provideHttpClient(),
    {
      provide: ErrorHandler,
      useClass: AppErrorHandler,
    },
    provideServiceWorker(
      'ngsw-worker.js',
      {
        enabled: !isDevMode(),
        registrationStrategy: 'registerWhenStable:30000',
      }),
  ],
};
