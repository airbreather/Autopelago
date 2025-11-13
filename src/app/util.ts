import { DestroyRef, effect, ElementRef, Injector, type Signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { JSONSerializable } from 'archipelago.js';
import { List } from 'immutable';
import { Observable, Subscription } from 'rxjs';

export type EnumVal<T extends object> = T[keyof T];

export type Mutable<T> = {
  -readonly [P in keyof T]: T[P];
};
export function shallowCloneAsMutable<T extends object>(obj: T): Mutable<T> {
  return { ...obj };
}

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
  return (Object.entries(obj) as Entry<T>[]).sort((a, b) => Number(a[0] > b[0]) - Number(a[0] < b[0]));
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

export type ToJSONSerializable<T> =
  T extends JSONSerializable ? T : {
    [K in keyof T]: T[K] extends JSONSerializable
      ? T[K]
      : T[K] extends (readonly (infer E)[])
        ? ToJSONSerializable<E>[]
        : T[K] extends List<infer E>
          ? ToJSONSerializable<E>[]
          : never;
  };

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
