import { CdkScrollable } from '@angular/cdk/overlay';
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';

import versionInfo from '../../../../version-info.json';
import type { AutopelagoClientAndData } from '../../../data/slot-data';

import { GameScreenStore, type GameTab } from '../../../store/game-screen-store';
import { GameTabAppBuildInfo } from './game-tab-app-build-info/game-tab-app-build-info';
import { GameTabArcade } from './game-tab-arcade/game-tab-arcade';
import { GameTabChat } from './game-tab-chat/game-tab-chat';
import { GameTabMap } from './game-tab-map/game-tab-map';

@Component({
  selector: 'app-game-tabs',
  imports: [
    CdkScrollable,
    GameTabAppBuildInfo,
    GameTabArcade,
    GameTabChat,
    GameTabMap,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer">
      <div class="top">
        <app-game-tab-map [class.current-tab]="currentTab() === 'map'" [hidden]="currentTab() !== 'map'" cdk-scrollable />
        <app-game-tab-arcade [class.current-tab]="currentTab() === 'arcade'" [hidden]="currentTab() !== 'arcade'" cdk-scrollable />
        <app-game-tab-chat [class.current-tab]="currentTab() === 'chat'" [hidden]="currentTab() !== 'chat'" [game]="game()" cdk-scrollable />
        <app-game-tab-app-build-info [class.current-tab]="currentTab() === 'app-build-info'" [hidden]="currentTab() !== 'app-build-info'" cdk-scrollable />
      </div>
      <div class="bottom">
        <button class="tab tab-map rat-toggle-button" [class.toggled-on]="currentTab() === 'map'" (click)="clickTab('map')">
          Map
        </button>
        <div class="tab tab-arcade rat-toggle-button" [class.toggled-on]="currentTab() === 'arcade'">
          Arcade
        </div>
        <button class="tab tab-chat rat-toggle-button" [class.toggled-on]="currentTab() === 'chat'" (click)="clickTab('chat')">
          Chat
        </button>
        <div class="tab-filler">
        </div>
        <button class="tab tab-app-build-info rat-toggle-button" [class.toggled-on]="currentTab() === 'app-build-info'" (click)="clickTab('app-build-info')">
          Build Info: {{version}}
        </button>
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

        .tab-chat {
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

        .tab-app-build-info {
          flex: 0;
          margin-right: 5px;
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

  protected readonly currentTab = this.#store.currentTab;
  protected readonly version = versionInfo.version;

  protected clickTab(tab: GameTab) {
    this.#store.updateCurrentTab(tab);
  }
}
