import { type CreateEffectOptions, effect, type ElementRef, type Signal, signal } from '@angular/core';
import { Observable } from 'rxjs';

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

export interface ResizeObserverEvent<T extends HTMLElement> {
  target: T;
  entries: ResizeObserverEntry[];
  observer: ResizeObserver;
}

export function resizeEvents<T extends HTMLElement>(el: T): Observable<ResizeObserverEvent<T>> {
  return new Observable<ResizeObserverEvent<T>>((subscriber) => {
    function next(this: T, entries: ResizeObserverEntry[], observer: ResizeObserver) {
      subscriber.next({ target: this, entries, observer });
    }

    const obs = new ResizeObserver(next.bind(el));
    obs.observe(el);
    return () => {
      obs.unobserve(el);
    };
  });
}
