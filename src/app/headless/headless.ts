import { Component, input } from '@angular/core';

import type { Client } from 'archipelago.js';

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
  protected readonly archipelago = input.required<Client>();

  // eslint-disable-next-line @typescript-eslint/no-useless-constructor
  constructor() {
    // empty
  }
}
