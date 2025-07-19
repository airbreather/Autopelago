import { Component, effect, inject, signal } from '@angular/core';

import { Ticker } from 'pixi.js';

import { PixiService } from '../pixi-service';

import { GameScreenStore } from '../../../../store/game-screen-store';

@Component({
  selector: 'app-pause-button',
  imports: [],
  template: `
    <button class="rat-toggle-button"
            [class.toggled-on]="paused()"
            (click)="togglePause()">
      ⏸
    </button>
  `,
  styles: '',
})
export class PauseButton {
  readonly #store = inject(GameScreenStore);

  readonly paused = this.#store.paused;

  readonly togglePause = this.#store.togglePause;

  constructor() {
    const ticker = signal<Ticker | null>(null);
    inject(PixiService).registerPlugin({
      afterInit: (app) => {
        // even if we start paused, the ticker needs to run once to get the initial frames.
        app.ticker.addOnce((t) => {
          ticker.set(t);
        });
      },
    });

    effect(() => {
      const theTicker = ticker();
      if (theTicker && (this.paused() === theTicker.started)) {
        if (this.paused()) {
          theTicker.stop();
        }
        else {
          theTicker.start();
        }
      }
    });
  }
}
