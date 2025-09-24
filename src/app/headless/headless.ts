import { Component, input } from '@angular/core';
import { AutopelagoService } from '../game/autopelago';

@Component({
  selector: 'app-headless',
  imports: [],
  template: `
    <p>
      check your browser console, bro!
    </p>
  `,
  styles: '',
})
export class Headless {
  protected readonly autopelago = input.required<AutopelagoService>();

  // eslint-disable-next-line @typescript-eslint/no-useless-constructor
  constructor() {
    // empty
  }
}
