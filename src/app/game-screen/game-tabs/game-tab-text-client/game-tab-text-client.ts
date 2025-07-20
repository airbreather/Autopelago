import { Component, inject } from '@angular/core';

import { ArchipelagoClient } from '../../../archipelago-client';
import { GameScreenStore } from '../../../store/game-screen-store';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'app-game-tab-text-client',
  imports: [],
  template: `
    <div class="outer">
      <div class="filler"></div>
      @for (message of messages(); track $index) {
        <p class="message">
          [{{ dateFormatter.format(message.ts) }}]
          @for (messageNode of message.originalNodes; track $index) {
            <!--
              Follow Yacht Dice:
              https://github.com/spinerak/YachtDiceAP/blob/936dde8034a88b653b71a2d653467203ea781d41/index.html#L4517-L4591
            -->
            @switch (messageNode.type) {
              @case ('color') {
                <span [style.color]="messageNode.color">
                  {{messageNode.text}}
                </span>
              }
              @default {
                <span>
                  {{messageNode.text}}
                </span>
              }
            }
          }
        </p>
      }
    </div>
  `,
  styles: `
    .outer {
      display: flex;
      flex-direction: column;
      height: 100%;
      --font-family: sans-serif;
    }

    .filler {
      flex: 1;
    }

    .message {
      flex: 0;
      margin-block-start: 0;
      margin-block-end: 0;
    }
  `,
})
export class GameTabTextClient {
  readonly #ap = inject(ArchipelagoClient);
  readonly #store = inject(GameScreenStore);
  readonly messages = this.#store.messages;
  readonly dateFormatter = new Intl.DateTimeFormat(navigator.languages[0], { dateStyle: 'short', timeStyle: 'medium' });

  constructor() {
    this.#ap.events('messages', 'message')
      .pipe(takeUntilDestroyed())
      .subscribe(([_, nodes]) => {
        this.#store.appendMessage({ ts: new Date(), originalNodes: nodes });
      });
  }
}
