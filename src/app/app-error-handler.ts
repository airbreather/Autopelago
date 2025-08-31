import { ErrorHandler, inject, Injectable } from '@angular/core';
import { ToastrService } from 'ngx-toastr';

@Injectable()
export class AppErrorHandler extends ErrorHandler {
  readonly #toast = inject(ToastrService);

  override handleError(error: unknown): void {
    if (error instanceof Error) {
      this.#toast.error(error.message, error.name, { toastClass: 'ngx-toastr error-message' });
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
