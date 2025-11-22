import { type CreateEffectOptions, effect, ElementRef, type Signal } from '@angular/core';

export interface ResizeTextOptions {
  outer: Signal<ElementRef<HTMLElement>>;
  inner: Signal<ElementRef<HTMLElement>>;
  max: number;
  createEffectOptions?: CreateEffectOptions;
}

export function resizeText({ outer, inner, max, createEffectOptions }: ResizeTextOptions) {
  effect((onCleanup) => {
    const outerElement = outer().nativeElement;
    const innerElement = inner().nativeElement;
    let prevTimeout: number | null = null;
    const obs = new ResizeObserver(() => {
      if (prevTimeout !== null) {
        clearTimeout(prevTimeout);
      }

      prevTimeout = setTimeout(() => {
        fitTextToContainer(innerElement, outerElement, max);
      }, 0);
    });
    obs.observe(outerElement);
    onCleanup(() => {
      obs.unobserve(outerElement);
    });
  }, createEffectOptions);
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
