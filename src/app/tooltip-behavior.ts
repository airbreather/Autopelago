import { CdkOverlayOrigin } from '@angular/cdk/overlay';
import { Directive, effect, ElementRef, inject, input, output } from '@angular/core';

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
  readonly #uid = Symbol();
  readonly #el = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly tooltipContext = input<TooltipContext>(createEmptyTooltipContext());
  readonly tooltipOriginChange = output<TooltipOriginProps | null>();

  readonly delay = input(400);

  constructor() {
    const enter = () => {
      this.onFocus(false);
    };
    const leave = () => {
      this.onBlur(false);
    };
    effect((onCleanup) => {
      const ctx = this.tooltipContext();
      ctx._notifyMouseEnterTooltipCallbacks.set(this.#uid, enter);
      ctx._notifyMouseLeaveTooltipCallbacks.set(this.#uid, leave);
      onCleanup(() => {
        ctx._notifyMouseEnterTooltipCallbacks.delete(this.#uid);
        ctx._notifyMouseLeaveTooltipCallbacks.delete(this.#uid);
      });
    });
  }

  protected onFocus(fromFocus: boolean) {
    const ctx = this.tooltipContext();
    if (!fromFocus && ctx._tooltipIsOpenBecauseOfFocus) {
      return;
    }

    if (fromFocus && getComputedStyle(this.#el.nativeElement).getPropertyValue('--ap-focus-visible') !== '1') {
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

  protected onBlur(fromFocus: boolean) {
    const ctx = this.tooltipContext();
    if (!fromFocus && ctx._tooltipIsOpenBecauseOfFocus) {
      return;
    }

    if (fromFocus && ctx._tooltipIsOpenBecauseOfFocus !== this) {
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
    ctx._tooltipIsOpenBecauseOfFocus = fromFocus ? this : null;
    ctx._tooltipIsOpenBecauseOfMouse = !fromFocus;
    this.tooltipOriginChange.emit({
      uid: this.#uid,
      element: this.#el.nativeElement,
      notifyDetached: this.#detachTooltip,
    });
  }

  #emitDetached(ctx: TooltipContext) {
    ctx._tooltipIsOpenBecauseOfFocus = null;
    ctx._tooltipIsOpenBecauseOfMouse = false;
    this.tooltipOriginChange.emit(null);
  }
}

export function createEmptyTooltipContext(): TooltipContext {
  const _notifyMouseEnterTooltipCallbacks = new Map<symbol, (this: void) => void>();
  const _notifyMouseLeaveTooltipCallbacks = new Map<symbol, (this: void) => void>();
  return {
    _prevFocusTimeout: NaN,
    _prevBlurTimeout: NaN,
    _tooltipIsOpenBecauseOfFocus: null,
    _tooltipIsOpenBecauseOfMouse: false,
    _notifyMouseEnterTooltipCallbacks,
    _notifyMouseLeaveTooltipCallbacks,
    notifyMouseEnterTooltip(uid: symbol) {
      _notifyMouseEnterTooltipCallbacks.get(uid)?.();
    },
    notifyMouseLeaveTooltip(uid: symbol) {
      _notifyMouseLeaveTooltipCallbacks.get(uid)?.();
    },
  };
}

export interface TooltipContext {
  _prevFocusTimeout: number;
  _prevBlurTimeout: number;
  _tooltipIsOpenBecauseOfFocus: TooltipBehavior | null;
  _tooltipIsOpenBecauseOfMouse: boolean;
  _notifyMouseEnterTooltipCallbacks: Map<symbol, (this: void) => void>;
  _notifyMouseLeaveTooltipCallbacks: Map<symbol, (this: void) => void>;
  notifyMouseEnterTooltip(uid: symbol): void;
  notifyMouseLeaveTooltip(uid: symbol): void;
}

export interface TooltipOriginProps {
  uid: symbol;
  element: HTMLElement;
  notifyDetached: () => void;
}
