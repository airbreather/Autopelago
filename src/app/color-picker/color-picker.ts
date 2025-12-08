import { Component, effect, model } from '@angular/core';
import { TinyColor } from '@ctrl/tinycolor/dist';

@Component({
  selector: 'app-color-picker',
  imports: [],
  template: `
    <input #col type="color" [value]="color().toHexString(true)" (input)="onInput(col.value)" />
  `,
  styles: `
  `,
})
export class ColorPicker {
  readonly color = model.required<TinyColor>();
  constructor() {
    effect(() => {
      const nextFrame = () => {
        this.color.update((c) => {
          const hsv = c.toHsv();
          hsv.h += Math.random() * 4;
          if (hsv.h > 360) {
            hsv.h -= 360;
          }
          return new TinyColor(hsv);
        });
        requestAnimationFrame(nextFrame);
      };
      nextFrame();
    });
  }

  protected onInput(val: string) {
    this.color.set(new TinyColor(val));
  }
}
