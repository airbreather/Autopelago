import { CdkScrollable } from '@angular/cdk/overlay';
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import type { AutopelagoClientAndData } from '../../../data/slot-data';

import { GameScreenStore, type GameTab } from '../../../store/game-screen-store';
import { GameTabArcade } from './game-tab-arcade/game-tab-arcade';
import { GameTabMap } from './game-tab-map/game-tab-map';
import { GameTabTextClient } from './game-tab-text-client/game-tab-text-client';

@Component({
  selector: 'app-game-tabs',
  imports: [
    CdkScrollable,
    GameTabMap,
    GameTabArcade,
    GameTabTextClient,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer">
      <div class="top">
        <app-game-tab-map [class.current-tab]="currentTab() === 'map'" [hidden]="currentTab() !== 'map'" cdk-scrollable />
        <app-game-tab-arcade [class.current-tab]="currentTab() === 'arcade'" [hidden]="currentTab() !== 'arcade'" cdk-scrollable />
        <app-game-tab-text-client [class.current-tab]="currentTab() === 'text-client'" [hidden]="currentTab() !== 'text-client'" [game]="game()" cdk-scrollable />
      </div>
      <div class="bottom">
        <button class="tab tab-map rat-toggle-button" [class.toggled-on]="currentTab() === 'map'" (click)="clickTab('map')">
          Map
        </button>
        <div class="tab tab-arcade rat-toggle-button" [class.toggled-on]="currentTab() === 'arcade'">
          Arcade
        </div>
        <button class="tab tab-text-client rat-toggle-button" [class.toggled-on]="currentTab() === 'text-client'" (click)="clickTab('text-client')">
          Text Client
        </button>
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
        overflow-y: hidden;
        > .current-tab {
          display: block;
          height: 100%;
          overflow-y: auto;
        }
      }

      .bottom {
        flex: 0;
        display: flex;

        .tab-map {
          flex: 0;
        }

        .tab-text-client {
          flex: 0;
        }

        .tab-arcade {
          flex: 0;
          color: #606060;
          pointer-events: none;
          user-select: none;
        }

        .tab-filler {
          flex: 1;
        }
      }

      .tab {
        border-top-left-radius: 0;
        border-top-right-radius: 0;
        padding: 5px;
        text-wrap: nowrap;

        &:not(:first-child) {
          margin-left: 5px;
        }
      }
    }`,
})
export class GameTabs {
  readonly #store = inject(GameScreenStore);

  readonly game = input.required<AutopelagoClientAndData>();

  readonly currentTab = this.#store.currentTab;

  clickTab(tab: GameTab) {
    this.#store.updateCurrentTab(tab);
  }
}
