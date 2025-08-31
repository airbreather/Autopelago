import { ErrorHandler, inject, Injectable } from '@angular/core';
import { IndividualConfig, ToastrService } from 'ngx-toastr';

const errorOptions: Partial<IndividualConfig<void>> = {
  toastClass: 'ngx-toastr error-message',
  disableTimeOut: true,
} as const;

@Injectable()
export class AppErrorHandler extends ErrorHandler {
  readonly #toast = inject(ToastrService);

  override handleError(error: unknown): void {
    if (error instanceof Error) {
      this.#toast.error(error.message, error.name, errorOptions);
    }
    else {
      this.#toast.error(
        'An unexpected error has occurred.',
        'Error',
      );
    }

    super.handleError(error);
  }
}
