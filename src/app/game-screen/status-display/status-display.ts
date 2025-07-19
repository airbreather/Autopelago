import { Component } from '@angular/core';

import { AurasDisplay } from './auras-display/auras-display';
import { PlayerNameAndNavigation } from './player-name-and-navigation/player-name-and-navigation';
import { ProgressionItemStatus } from './progression-item-status/progression-item-status';

@Component({
  selector: 'app-status-display',
  imports: [
    AurasDisplay, PlayerNameAndNavigation, ProgressionItemStatus,
  ],
  template: `
    <div class="outer">
      <div class="top">
        <app-player-name-and-navigation />
        <div class="auras">
          <app-auras-display />
        </div>
      </div>
      <hr class="divider" />
      <app-progression-item-status class="bottom" />
    </div>
  `,
  styles: `
    .outer {
      display: flex;
      flex-direction: column;
    }

    .top {
      flex: 0;
    }

    .auras {
      margin-top: 10px;
    }

    .divider {
      color: #666666;
      width: 100%;
      margin-top: 10px;
      margin-bottom: 10px;
    }

    .bottom {
      flex: 1;
    }
  `,
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class StatusDisplay {
}
