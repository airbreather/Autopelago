import { Observable, retry, Subscription, timer } from 'rxjs';
import { DestroyRef, effect, ElementRef, Injector, Signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

// BEGIN section that was discovered by:
// https://dev.to/harry0000/a-bit-convenient-typescript-type-definitions-for-objectentries-d6g
type TupleEntry<T extends readonly unknown[], I extends unknown[] = [], R = never> =
  T extends readonly [infer Head, ...infer Tail]
    ? TupleEntry<Tail, [...I, unknown], R | [`${I['length']}`, Head]>
    : R;

type ObjectEntry<T extends object> =
  T extends object
    ? { [K in keyof T]: [K, Required<T>[K]] }[keyof T] extends infer E
        ? E extends [infer K, infer V]
          ? K extends string | number
            ? [`${K}`, V]
            : never
          : never
        : never
    : never;

export type Entry<T extends object> =
  T extends readonly [unknown, ...unknown[]]
    ? TupleEntry<T>
    : T extends readonly (infer U)[]
      ? [`${number}`, U]
      : ObjectEntry<T>;

export function strictObjectEntries<T extends object>(obj: T): Entry<T>[] {
  return Object.entries(obj) as Entry<T>[];
}

// END section that was discovered by:
// https://dev.to/harry0000/a-bit-convenient-typescript-type-definitions-for-objectentries-d6g

export function stricterObjectFromEntries<T extends object, V>(entries: [k: keyof T, v: V][]): Record<keyof T, V> {
  return Object.fromEntries(entries) as Record<keyof T, V>;
}

export function stricterIsArray<T>(value: T): value is Extract<T, readonly unknown[]> {
  return Array.isArray(value);
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

const DEFAULT_RETRY_DELAY = 500;
const DEFAULT_RETRY_MAX_DELAY = 15000;
const DEFAULT_RETRY_CONFIG = { delay: DEFAULT_RETRY_DELAY, maxDelay: DEFAULT_RETRY_MAX_DELAY };

export function retryWithExponentialBackoff<T>({ delay, maxDelay } = DEFAULT_RETRY_CONFIG) {
  return (obs: Observable<T>) => obs.pipe(
    retry({
      delay: (_, retryCount) => timer(Math.min(maxDelay, Math.pow(2, retryCount - 1) * delay)),
    }),
  );
}

interface ResizeTextOptions {
  outer: Signal<ElementRef<HTMLElement>>;
  inner: Signal<ElementRef<HTMLElement>>;
  destroy: DestroyRef;
  injector: Injector;
  max: number;
}

export function resizeText({ outer, inner, destroy, injector, max }: ResizeTextOptions) {
  let prevSub = new Subscription();
  effect(() => {
    let prevTimeout: number | undefined;
    prevSub.unsubscribe();
    const outerElement = outer().nativeElement;
    const innerElement = inner().nativeElement;
    prevSub = resizeEvents(outerElement)
      .pipe(takeUntilDestroyed(destroy))
      .subscribe(() => {
        clearTimeout(prevTimeout);
        prevTimeout = setTimeout(() => {
          fitTextToContainer(innerElement, outerElement, max);
        }, 0);
      });
  }, { injector });
}

function fitTextToContainer(inner: HTMLElement, outer: HTMLElement, max: number) {
  let fontSize = window.getComputedStyle(inner).fontSize;

  while (inner.scrollWidth <= outer.clientWidth) {
    const fontSizeNum = Math.min(max, Number(/^\d+/.exec(fontSize)) + 5);
    if (fontSizeNum >= max) {
      break;
    }

    fontSize = fontSize.replace(/^\d+/, fontSizeNum.toString());
    inner.style.fontSize = fontSize;
  }

  while (inner.scrollWidth > outer.clientWidth) {
    fontSize = fontSize.replace(/^\d+/, s => (Number(s) - 1).toString());
    inner.style.fontSize = fontSize;
  }
}
