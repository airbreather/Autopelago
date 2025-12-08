import { Component, model } from '@angular/core';
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
  protected onInput(val: string) {
    this.color.set(new TinyColor(val));
  }
}
