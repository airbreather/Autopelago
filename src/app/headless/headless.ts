import { Component, effect, input } from '@angular/core';
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

  constructor() {
    effect(() => {
      this.autopelago().rawClient.events('messages', 'message')
        .subscribe(([message]) => {
          console.log('Whoa, a message! I should write it out now:', message);
        });
    });
  }
}
