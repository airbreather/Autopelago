import { ChangeDetectionStrategy, Component, computed, model } from '@angular/core';
import { type ColorInput, stringInputToObject, TinyColor } from '@ctrl/tinycolor';

@Component({
  selector: 'app-color-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <div class="sketch-picker">
      <div class="sketch-saturation">
        <div
          class="color-saturation"
          [style.background]="background()">
          <div class="saturation-white"
               (pointerdown)="onDragStart(saturationPointer, $event)"
               (pointermove)="onDrag(saturationPointer, $event)"
               (pointerup)="onDragEnd(saturationPointer, $event)">
            <div class="saturation-black"></div>
            <div
              #saturationPointer
              class="saturation-pointer"
              [style.top.%]="valuePercentageFrom100()"
              [style.left.%]="saturationPercentage()">
              <div class="saturation-circle"></div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: `
    .sketch-picker {
      box-sizing: initial;
      border-radius: 4px;
      width: 100%;
    }

    .sketch-saturation {
      width: 100%;
      padding-bottom: 75%;
      position: relative;
      overflow: hidden;
    }

    .sketch-fields-container {
      display: block;
    }

    .sketch-swatches-container {
      display: block;
    }

    .sketch-controls {
      display: flex;
    }

    .sketch-sliders {
      padding: 4px 0;
      -webkit-box-flex: 1;
      flex: 1 1 0;
    }

    .sketch-hue {
      position: relative;
      height: 10px;
      overflow: hidden;
    }

    .sketch-alpha {
      position: relative;
      height: 10px;
      margin-top: 4px;
      overflow: hidden;
    }

    .sketch-color {
      width: 24px;
      height: 24px;
      position: relative;
      margin-top: 4px;
      margin-left: 4px;
      border-radius: 3px;
    }

    .sketch-active {
      position: absolute;
      top: 0;
      right: 0;
      bottom: 0;
      left: 0;
      border-radius: 2px;
      box-shadow: rgba(0, 0, 0, 0.15) 0 0 0 1px inset,
      rgba(0, 0, 0, 0.25) 0 0 4px inset;
    }

    :host-context([dir='rtl']) .sketch-color {
      margin-right: 4px;
      margin-left: 0;
    }

    .saturation-white {
      background: linear-gradient(to right, #ffffff, rgba(255, 255, 255, 0));
      position: absolute;
      top: 0;
      bottom: 0;
      left: 0;
      right: 0;
    }

    .saturation-black {
      background: linear-gradient(to top, #000000, rgba(0, 0, 0, 0));
      position: absolute;
      top: 0;
      bottom: 0;
      left: 0;
      right: 0;
    }

    .color-saturation {
      position: absolute;
      top: 0;
      bottom: 0;
      left: 0;
      right: 0;
    }

    .saturation-pointer {
      position: absolute;
    }

    .saturation-circle {
      width: 4px;
      height: 4px;
      box-shadow: 0 0 0 1.5px #ffffff,
      inset 0 0 1px 1px rgba(0, 0, 0, 0.3),
      0 0 1px 2px rgba(0, 0, 0, 0.4);
      border-radius: 50%;
      transform: translate(-2px, -4px);
    }
  `,
})
export class ColorPicker {
  readonly color = model.required<ColorInput>();
  readonly #colorObject = computed(() => {
    const a = this.color();
    if (typeof a === 'object') {
      return a;
    }
    if (typeof a === 'string') {
      const o = stringInputToObject(a) as ColorInput | false;
      if (typeof o === 'object') {
        return o;
      }
    }
    return new TinyColor(a);
  });

  protected readonly h = computed(() => {
    const a = this.#colorObject();
    return 'h' in a
      ? a.h
      : new TinyColor(a).toHsv().h;
  });

  protected readonly s = computed(() => {
    const a = this.#colorObject();
    return 's' in a
      ? a.s
      : new TinyColor(a).toHsv().s;
  });

  protected readonly v = computed(() => {
    const a = this.#colorObject();
    return 'v' in a
      ? a.v
      : new TinyColor(a).toHsv().v;
  });

  protected readonly l = computed(() => {
    const a = this.#colorObject();
    return 'l' in a
      ? a.l
      : new TinyColor(a).toHsl().l;
  });

  protected readonly background = computed(() => new TinyColor({ h: this.h(), s: 1, l: 0.5 }).toHslString());
  protected readonly valuePercentageFrom100 = computed(() => -(Number(this.v()) * 100) + 1 + 100);
  protected readonly saturationPercentage = computed(() => Number(this.s()) * 100);

  protected onDragStart(pointer: HTMLElement, event: PointerEvent) {
    pointer.setPointerCapture(event.pointerId);
    this.onDrag(pointer, event);
  }

  protected onDrag(pointer: HTMLElement, event: PointerEvent) {
    if (!pointer.hasPointerCapture(event.pointerId)) {
      return;
    }
    const { left: pLeft, top: pTop, width: pWidth, height: pHeight } = (pointer.parentElement as unknown as HTMLDivElement).getBoundingClientRect();
    const v = 1 - Math.min(Math.max((event.pageY - pTop) / pHeight, 0), 1);
    const s = Math.min(Math.max((event.pageX - pLeft) / pWidth, 0), 1);
    this.color.set({ h: this.h(), v, s });
    event.preventDefault();
  }

  protected onDragEnd(pointer: HTMLElement, event: PointerEvent) {
    if (!pointer.hasPointerCapture(event.pointerId)) {
      return;
    }
    pointer.releasePointerCapture(event.pointerId);
  }
}
