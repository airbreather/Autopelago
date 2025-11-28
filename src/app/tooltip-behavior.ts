import { CdkOverlayOrigin } from '@angular/cdk/overlay';
import { Directive, ElementRef, inject, input, output } from '@angular/core';

@Directive({
  selector: '[appTooltip]',
  host: {
    '(focus)': 'onFocus(true)',
    '(mouseenter)': 'onFocus(false)',
    '(blur)': 'onBlur(true)',
    '(mouseleave)': 'onBlur(false)',
  },
  hostDirectives: [
    CdkOverlayOrigin,
  ],
})
export class TooltipBehavior {
  readonly #el = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly tooltipContext = input<TooltipContext>(createEmptyTooltipContext());
  readonly tooltipOriginChange = output<TooltipOriginProps | null>();

  readonly delay = input(400);

  onFocus(fromFocus: boolean) {
    const ctx = this.tooltipContext();
    if (!fromFocus && ctx._tooltipIsOpenBecauseOfFocus) {
      return;
    }

    if (fromFocus && ctx._tooltipIsOpenBecauseOfMouse) {
      this.#emitDetached(ctx);
    }

    if (!Number.isNaN(ctx._prevBlurTimeout)) {
      clearTimeout(ctx._prevBlurTimeout);
      ctx._prevBlurTimeout = NaN;
      this.#emitAttached(fromFocus, ctx);
      return;
    }

    if (!Number.isNaN(ctx._prevFocusTimeout)) {
      clearTimeout(ctx._prevFocusTimeout);
      ctx._prevFocusTimeout = NaN;
    }
    if (fromFocus) {
      this.#emitAttached(fromFocus, ctx);
    }
    else {
      ctx._prevFocusTimeout = setTimeout(() => {
        this.#emitAttached(fromFocus, ctx);
        ctx._prevFocusTimeout = NaN;
      }, this.delay());
    }
  }

  onBlur(fromFocus: boolean) {
    const ctx = this.tooltipContext();
    if (!fromFocus && ctx._tooltipIsOpenBecauseOfFocus) {
      return;
    }

    if (!Number.isNaN(ctx._prevFocusTimeout)) {
      clearTimeout(ctx._prevFocusTimeout);
      ctx._prevFocusTimeout = NaN;
      this.#emitDetached(ctx);
      return;
    }

    if (!Number.isNaN(ctx._prevBlurTimeout)) {
      clearTimeout(ctx._prevBlurTimeout);
      ctx._prevBlurTimeout = NaN;
    }
    if (fromFocus) {
      this.#emitDetached(ctx);
    }
    else {
      ctx._prevBlurTimeout = setTimeout(() => {
        this.#emitDetached(ctx);
        ctx._prevBlurTimeout = NaN;
      }, this.delay());
    }
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

    this.#emitDetached(ctx);
  };

  #emitAttached(fromFocus: boolean, ctx: TooltipContext) {
    ctx._tooltipIsOpenBecauseOfFocus = fromFocus;
    ctx._tooltipIsOpenBecauseOfMouse = !fromFocus;
    this.tooltipOriginChange.emit({
      element: this.#el.nativeElement,
      notifyDetached: this.#detachTooltip,
    });
  }

  #emitDetached(ctx: TooltipContext) {
    ctx._tooltipIsOpenBecauseOfFocus = false;
    ctx._tooltipIsOpenBecauseOfMouse = false;
    this.tooltipOriginChange.emit(null);
  }
}

export function createEmptyTooltipContext(): TooltipContext {
  return {
    _prevFocusTimeout: NaN,
    _prevBlurTimeout: NaN,
    _tooltipIsOpenBecauseOfFocus: false,
    _tooltipIsOpenBecauseOfMouse: false,
  };
}

export interface TooltipContext {
  _prevFocusTimeout: number;
  _prevBlurTimeout: number;
  _tooltipIsOpenBecauseOfFocus: boolean;
  _tooltipIsOpenBecauseOfMouse: boolean;
}

export interface TooltipOriginProps {
  element: HTMLElement;
  notifyDetached: () => void;
}
