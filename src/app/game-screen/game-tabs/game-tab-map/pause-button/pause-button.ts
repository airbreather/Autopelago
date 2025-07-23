import { Component, inject } from '@angular/core';

import { GameStore } from '../../../../store/autopelago-store';

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
  readonly #store = inject(GameStore);

  readonly paused = this.#store.paused;

  readonly togglePause = this.#store.togglePause;
}
