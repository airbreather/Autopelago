import { type CreateEffectOptions, effect, ElementRef, type Signal } from '@angular/core';
import { binarySearch } from './binary-search';

export interface ResizeTextOptions {
  outer: Signal<ElementRef<HTMLElement>>;
  inner: Signal<ElementRef<HTMLElement>>;
  text: Signal<string>;
  max: number;
  createEffectOptions?: CreateEffectOptions;
}

export function resizeText({ outer, inner, text, max, createEffectOptions }: ResizeTextOptions) {
  effect((onCleanup) => {
    const outerElement = outer().nativeElement;
    const innerElement = inner().nativeElement;
    const { paddingLeft, paddingRight } = getComputedStyle(innerElement);
    const canvas = new OffscreenCanvas(9999, 9999);
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      return;
    }
    const sizes = Array<number>((max * 2) + 1).fill(parseInt(paddingLeft) + parseInt(paddingRight) + 5);
    const t = text();
    for (let i = 0; i < sizes.length; i++) {
      ctx.font = `${(i / 2).toString()}px PublicPixel`;
      sizes[i] += ctx.measureText(t).width;
    }
    let prevTimeout: number | null = null;
    const obs = new ResizeObserver(() => {
      if (prevTimeout !== null) {
        clearTimeout(prevTimeout);
      }

      prevTimeout = setTimeout(() => {
        const i = binarySearch(sizes, outerElement.clientWidth);
        innerElement.style.fontSize = `${(i / 2).toString()}px`;
      }, 0);
    });
    obs.observe(outerElement);
    onCleanup(() => {
      obs.unobserve(outerElement);
    });
  }, createEffectOptions);
}
