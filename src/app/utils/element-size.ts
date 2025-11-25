import { type CreateEffectOptions, effect, type ElementRef, type Signal, signal } from '@angular/core';

export interface ElementSize {
  clientWidth: number;
  clientHeight: number;
  scrollWidth: number;
  scrollHeight: number;
}

export function elementSizeSignal(provider: Signal<ElementRef<HTMLElement>>, options?: CreateEffectOptions) {
  const result = signal<ElementSize>({ clientWidth: 0, clientHeight: 0, scrollWidth: 0, scrollHeight: 0 });
  effect((onCleanup) => {
    const el = provider().nativeElement;
    const obs = new ResizeObserver(() => {
      const { clientWidth, clientHeight, scrollWidth, scrollHeight } = el;
      result.set({ clientWidth, clientHeight, scrollWidth, scrollHeight });
    });
    obs.observe(el);
    onCleanup(() => {
      obs.unobserve(el);
    });
  }, options);
  return result.asReadonly();
}
