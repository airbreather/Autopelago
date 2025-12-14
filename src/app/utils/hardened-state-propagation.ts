import { type ColorInput, TinyColor } from '@ctrl/tinycolor';

export function trySetArrayProp<TKey extends string | number | symbol>(source: Partial<Record<TKey, unknown>>, key: TKey, target: Partial<Record<TKey, unknown[]>>, isValid?: (x: unknown, i: number, a: unknown[]) => boolean) {
  if (key in source && Array.isArray(source[key])) {
    const a: unknown[] = source[key];
    if (isValid) {
      for (let i = 0; i < a.length; i++) {
        if (!isValid(a[i], i, a)) {
          return;
        }
      }
    }
    target[key] = source[key];
  }
}

export function trySetBooleanProp<TKey extends string | number | symbol>(source: Partial<Record<TKey, unknown>>, key: TKey, target: Partial<Record<TKey, boolean>>) {
  if (key in source && typeof source[key] === 'boolean') {
    target[key] = source[key];
  }
}

export function trySetNumberProp<TKey extends string | number | symbol>(source: Partial<Record<TKey, unknown>>, key: TKey, target: Partial<Record<TKey, number>>, isValid?: (n: number) => boolean) {
  if (key in source && typeof source[key] === 'number' && isValid?.(source[key]) !== false) {
    target[key] = source[key];
  }
}

export function trySetStringProp<TKey extends string | number | symbol>(source: Partial<Record<TKey, unknown>>, key: TKey, target: Partial<Record<TKey, string>>, isValid?: (s: string) => boolean) {
  if (key in source && typeof source[key] === 'string' && isValid?.(source[key]) !== false) {
    target[key] = source[key];
  }
}

export function trySetColorProp<TKey extends string | number | symbol>(source: Partial<Record<TKey, unknown>>, key: TKey, target: Partial<Record<TKey, TinyColor>>) {
  if (key in source) {
    // there are enough ColorInput types, and few enough straightforward "is this ColorInput legal?"
    // methods, that it's simplest and easiest to just try/catch.
    try {
      const color = new TinyColor(source[key] as ColorInput);
      if (color.isValid) {
        target[key] = color;
      }
    }
    catch {
      // this will happen if you pass something like a bigint or whatever.
    }
  }
}
