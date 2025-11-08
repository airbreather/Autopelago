import { ErrorHandler, inject, Injectable } from '@angular/core';
import { type IndividualConfig, ToastrService } from 'ngx-toastr';

const errorOptions: Partial<IndividualConfig<void>> = {
  toastClass: 'ngx-toastr error-message',
  disableTimeOut: true,
} as const;

export function toastError(toast: ToastrService, error: unknown, extraOptions?: Partial<IndividualConfig<void>>) {
  return error instanceof Error
    ? toast.error(error.message, error.name, { ...errorOptions, ...extraOptions })
    : toast.error('An unexpected error has occurred.', 'Error', { ...errorOptions, ...extraOptions });
}

@Injectable()
export class AppErrorHandler extends ErrorHandler {
  readonly #toast = inject(ToastrService);

  override handleError(error: unknown): void {
    toastError(this.#toast, error);
    super.handleError(error);
  }
}
