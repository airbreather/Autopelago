import { Component, inject } from '@angular/core';
import { GameTabMap } from './game-tab-map/game-tab-map';
import { GameScreenStore } from '../../store/game-screen-store';
import { GameTabArcade } from './game-tab-arcade/game-tab-arcade';

@Component({
  selector: 'app-game-tabs',
  imports: [
    GameTabMap,
    GameTabArcade,
  ],
  template: `
    <div class="outer">
      <div class="top">
        @switch (currentTab()) {
          @case ('map') {
            <app-game-tab-map />
          }
          @case ('arcade') {
            <app-game-tab-arcade />
          }
        }
      </div>
      <div class="bottom">
        <div class="tab-map">
          Map
        </div>
        <div class="tab-arcade">
          Arcade
        </div>
        <div class="tab-filler">
        </div>
      </div>
    </div>
  `,
  styles: `
    .outer {
      display: flex;
      flex-direction: column;
      height: 100%;

      .top {
        flex: 1;
        overflow: auto;
      }

      .bottom {
        flex: 0;
        display: flex;

        .tab-map {
          flex: 0;
        }

        .tab-arcade {
          flex: 0;
        }

        .tab-filler {
          flex: 1;
        }
      }
    }`,
})
export class GameTabs {
  readonly #store = inject(GameScreenStore);

  readonly currentTab = this.#store.currentTab;
}
