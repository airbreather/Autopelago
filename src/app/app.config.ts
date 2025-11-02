import { provideHttpClient } from '@angular/common/http';
import {
  type ApplicationConfig,
  ErrorHandler,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';

import { provideAnimations } from '@angular/platform-browser/animations';
import { provideRouter, withComponentInputBinding, withHashLocation } from '@angular/router';

import { provideToastr } from 'ngx-toastr';
import { AppErrorHandler } from './app-error-handler';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    // eslint-disable-next-line @typescript-eslint/no-deprecated
    provideAnimations(),
    provideToastr({
      positionClass: 'toast-top-full-width',
      closeButton: true,
      progressBar: true,
    }),
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withHashLocation(), withComponentInputBinding()),
    provideHttpClient(),
    {
      provide: ErrorHandler,
      useClass: AppErrorHandler,
    },
  ],
};
