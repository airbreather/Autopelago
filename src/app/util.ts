import { Observable, retry, timer } from 'rxjs';

export function strictObjectEntries<T extends object>(obj: T): [keyof T, T[keyof T]][] {
  return Object.entries(obj) as [keyof T, T[keyof T]][];
}

export function stricterObjectFromEntries<T extends object, V>(entries: [k: keyof T, v: V][]): Record<keyof T, V> {
  return Object.fromEntries(entries) as Record<keyof T, V>;
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
