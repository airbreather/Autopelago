import { ChangeDetectionStrategy, Component, computed, model, signal } from '@angular/core';
import { type ColorInput, stringInputToObject, TinyColor } from '@ctrl/tinycolor';

@Component({
  selector: 'app-color-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <div class="outer">
      <div class="saturation"
           [style.background]="svBackground()"
           (pointerdown)="onDragDotStart(saturationPointer, $event)"
           (pointermove)="onDragDot(saturationPointer, $event)"
           (pointerup)="onDragDotEnd(saturationPointer, $event)">
        <div class="gradient-white"></div>
        <div class="gradient-black"></div>
        <div
          #saturationPointer
          class="saturation-pointer"
          [style.top.%]="valuePercentageFrom100()"
          [style.left.%]="saturationPercentage()">
        </div>
      </div>
      <div class="hue-and-square">
        <div class="hue-slider"
             (pointerdown)="onDragHueStart(huePointer, $event)"
             (pointermove)="onDragHue(huePointer, $event)"
             (pointerup)="onDragHueEnd(huePointer, $event)">
          <div #huePointer
               class="hue-pointer"
               [style.left.%]="huePercentage()">
          </div>
        </div>
        <div class="color-square" [style.background]="selectionBackground()">
        </div>
      </div>
      <div class="debug-display">
        <span>h:</span><span>{{ round3(h()) }}</span>
        <span>s:</span><span>{{ round3(s()) }}</span>
        <span>v:</span><span>{{ round3(v()) }}</span>
        <span>l:</span><span>{{ round3(l()) }}</span>
        <span>r:</span><span>{{ round3(r()) }}</span>
        <span>g:</span><span>{{ round3(g()) }}</span>
        <span>b:</span><span>{{ round3(b()) }}</span>
        <span>str:</span><input #strBox type="text" [value]="str()" (input)="updateStr(strBox.value)">
      </div>
    </div>
  `,
  styles: `
    .outer {
      border-radius: 4px;
      width: 100%;

      .saturation {
        width: 100%;
        height: 300px;
        display: grid;
        position: relative;

        .gradient-white, .gradient-black {
          grid-row: 1;
          grid-column: 1;
          width: 100%;
          height: 100%;
        }

        .gradient-white {
          background: linear-gradient(to right, #ffffff, rgba(255, 255, 255, 0));
        }

        .gradient-black {
          background: linear-gradient(to top, #000000, rgba(0, 0, 0, 0));
        }

        .saturation-pointer {
          position: absolute;
          width: 4px;
          height: 4px;
          box-shadow: 0 0 0 1.5px #ffffff,
          inset 0 0 1px 1px rgba(0, 0, 0, 0.3),
          0 0 1px 2px rgba(0, 0, 0, 0.4);
          border-radius: 50%;
          transform: translate(-2px, -4px);
        }
      }

      .hue-and-square {
        margin-top: 4px;
        display: flex;
        gap: 4px;
        width: 100%;
        height: 24px;

        .hue-slider {
          -webkit-box-flex: 1;
          flex: 1 1 0;
          height: 100%;
          position: relative;
          background: linear-gradient(
              to right,
              #ff0000 0%,
              #ffff00 17%,
              #00ff00 33%,
              #00ffff 50%,
              #0000ff 67%,
              #ff00ff 83%,
              #ff0000 100%
          );

          .hue-pointer {
            position: absolute;
            margin: 1px 0;
            width: 4px;
            border-radius: 1px;
            height: calc(100% - 2px);
            box-shadow: 0 0 2px rgba(0, 0, 0, 0.6);
            background: #ffffff;
            transform: translateX(-2px);
          }
        }

        .color-square {
          width: 24px;
          height: 100%;
          border-radius: 3px;
          box-shadow: rgba(0, 0, 0, 0.15) 0 0 0 1px inset,
          rgba(0, 0, 0, 0.25) 0 0 4px inset;
        }
      }

      .debug-display {
        display: grid;
        grid-template-columns: auto min-content;
        grid-auto-rows: auto;
        margin: 10px;
      }
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

  protected readonly r = computed(() => {
    const a = this.#colorObject();
    return 'r' in a
      ? a.r
      : new TinyColor(a).toRgb().r;
  });

  protected readonly g = computed(() => {
    const a = this.#colorObject();
    return 'g' in a
      ? a.g
      : new TinyColor(a).toRgb().g;
  });

  protected readonly b = computed(() => {
    const a = this.#colorObject();
    return 'b' in a
      ? a.b
      : new TinyColor(a).toRgb().b;
  });

  readonly #unvalidatedStr = signal<string | null>(null);
  protected readonly str = computed(() => {
    const s = this.#unvalidatedStr();
    if (s !== null) {
      return s;
    }
    const a = this.color();
    return typeof a === 'string'
      ? a
      : new TinyColor(this.color()).toHexString();
  });

  protected updateStr(input: string) {
    this.#unvalidatedStr.set(input);
    const validated = stringInputToObject(input) as ColorInput | false;
    if (validated !== false) {
      this.color.set(validated);
    }
  }

  protected readonly svBackground = computed(() => new TinyColor({ h: this.h(), s: 1, l: 0.5 }).toHslString());
  protected readonly selectionBackground = computed(() => new TinyColor(this.color()).toRgbString());
  protected readonly valuePercentageFrom100 = computed(() => -(Number(this.v()) * 100) + 1 + 100);
  protected readonly saturationPercentage = computed(() => Number(this.s()) * 100);
  protected readonly huePercentage = computed(() => Number(this.h()) * (1 / 3.6));

  protected onDragDotStart(pointer: HTMLElement, event: PointerEvent) {
    pointer.setPointerCapture(event.pointerId);
    this.onDragDot(pointer, event);
  }

  protected onDragDot(pointer: HTMLElement, event: PointerEvent) {
    if (!pointer.hasPointerCapture(event.pointerId)) {
      return;
    }
    const { left: pLeft, top: pTop, width: pWidth, height: pHeight } = (pointer.parentElement as unknown as HTMLDivElement).getBoundingClientRect();
    const v = 1 - Math.min(Math.max((event.pageY - pTop) / pHeight, 0), 1);
    const s = Math.min(Math.max((event.pageX - pLeft) / pWidth, 0), 1);
    this.#unvalidatedStr.set(null);
    this.color.set({ h: this.h(), v, s });
    event.preventDefault();
  }

  protected onDragDotEnd(pointer: HTMLElement, event: PointerEvent) {
    if (!pointer.hasPointerCapture(event.pointerId)) {
      return;
    }
    pointer.releasePointerCapture(event.pointerId);
  }

  protected onDragHueStart(pointer: HTMLElement, event: PointerEvent) {
    pointer.setPointerCapture(event.pointerId);
    this.onDragHue(pointer, event);
  }

  protected onDragHue(pointer: HTMLElement, event: PointerEvent) {
    if (!pointer.hasPointerCapture(event.pointerId)) {
      return;
    }
    const { left: pLeft, width: pWidth } = (pointer.parentElement as unknown as HTMLDivElement).getBoundingClientRect();
    const h = Math.min(Math.max((event.pageX - pLeft) / pWidth, 0), 1) * 360;
    this.#unvalidatedStr.set(null);
    this.color.set({ h, v: this.v(), s: this.s() });
    event.preventDefault();
  }

  protected onDragHueEnd(pointer: HTMLElement, event: PointerEvent) {
    if (!pointer.hasPointerCapture(event.pointerId)) {
      return;
    }
    pointer.releasePointerCapture(event.pointerId);
  }

  protected round3(num: string | number) {
    return Math.round(Number(num) * 1000) / 1000;
  }
}
