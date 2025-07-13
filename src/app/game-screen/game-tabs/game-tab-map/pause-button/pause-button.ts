import { Component, inject } from '@angular/core';
import { GameScreenStore } from "../../../../store/game-screen-store";

@Component({
  selector: 'app-pause-button',
  imports: [],
  template: `
    <div class="outer">
      <button class="rat-toggle-button"
              [class.toggled-on]="paused()"
              (click)="togglePause()">
        ⏸
      </button>
    </div>
  `,
  styles: `
    .outer {
      position: sticky;
      left: 5px;
      bottom: 5px;
      pointer-events: initial;
      width: fit-content;
    }
  `,
})
export class PauseButton {
  readonly #store = inject(GameScreenStore);

  readonly paused = this.#store.paused;

  readonly togglePause = this.#store.togglePause;
}
