import { CdkOverlayOrigin } from '@angular/cdk/overlay';
import { Directive, ElementRef, inject, input, output } from '@angular/core';

@Directive({
  selector: '[appTooltip]',
  host: {
    '(focus)': 'onFocus()',
    '(mouseenter)': 'onFocus()',
    '(blur)': 'onBlur()',
    '(mouseleave)': 'onBlur()',
  },
  hostDirectives: [
    CdkOverlayOrigin,
  ],
})
export class TooltipBehavior {
  readonly #el = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly tooltipContext = input<TooltipContext>({ _prevFocusTimeout: NaN, _prevBlurTimeout: NaN });
  readonly tooltipOriginChange = output<TooltipOriginProps | null>();

  readonly delay = input(400);

  onFocus() {
    const ctx = this.tooltipContext();
    if (!Number.isNaN(ctx._prevBlurTimeout)) {
      clearTimeout(ctx._prevBlurTimeout);
      ctx._prevBlurTimeout = NaN;
      this.#emitAttached();
      return;
    }

    if (!Number.isNaN(ctx._prevFocusTimeout)) {
      clearTimeout(ctx._prevFocusTimeout);
      ctx._prevFocusTimeout = NaN;
    }
    ctx._prevFocusTimeout = setTimeout(() => {
      this.#emitAttached();
      ctx._prevFocusTimeout = NaN;
    }, this.delay());
  }

  onBlur() {
    const ctx = this.tooltipContext();
    if (!Number.isNaN(ctx._prevFocusTimeout)) {
      clearTimeout(ctx._prevFocusTimeout);
      ctx._prevFocusTimeout = NaN;
      this.tooltipOriginChange.emit(null);
      return;
    }

    if (!Number.isNaN(ctx._prevBlurTimeout)) {
      clearTimeout(ctx._prevBlurTimeout);
      ctx._prevBlurTimeout = NaN;
    }
    ctx._prevBlurTimeout = setTimeout(() => {
      this.tooltipOriginChange.emit(null);
      ctx._prevBlurTimeout = NaN;
    }, this.delay());
  }

  readonly #detachTooltip = () => {
    const ctx = this.tooltipContext();
    if (!Number.isNaN(ctx._prevFocusTimeout)) {
      clearTimeout(ctx._prevFocusTimeout);
      ctx._prevFocusTimeout = NaN;
    }

    if (!Number.isNaN(ctx._prevBlurTimeout)) {
      clearTimeout(ctx._prevBlurTimeout);
      ctx._prevBlurTimeout = NaN;
    }

    this.tooltipOriginChange.emit(null);
  };

  #emitAttached() {
    this.tooltipOriginChange.emit({
      element: this.#el.nativeElement,
      notifyDetached: this.#detachTooltip,
    });
  }
}

export function createEmptyTooltipContext(): TooltipContext {
  return { _prevFocusTimeout: NaN, _prevBlurTimeout: NaN };
}

export interface TooltipContext {
  _prevFocusTimeout: number;
  _prevBlurTimeout: number;
}

export interface TooltipOriginProps {
  element: HTMLElement;
  notifyDetached: () => void;
}
