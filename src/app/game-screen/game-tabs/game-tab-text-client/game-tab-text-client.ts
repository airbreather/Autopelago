import { Component, effect, input, signal } from '@angular/core';
import { Player } from 'archipelago.js';
import type { AutopelagoClientAndData } from '../../../data/slot-data';

@Component({
  selector: 'app-game-tab-text-client',
  imports: [],
  template: `
    <div class="outer">
      <div class="filler"></div>
      @for (message of game().messageLog(); track $index) {
        <p class="message">
          [{{ dateFormatter.format(message.ts) }}]
          @for (messageNode of message.nodes; track $index) {
            <!--
              Follow Yacht Dice:
              https://github.com/spinerak/YachtDiceAP/blob/936dde8034a88b653b71a2d653467203ea781d41/index.html#L4517-L4591
            -->
            @switch (messageNode.type) {
              @case ('entrance') {
                <span class="entrance-message">{{ messageNode.text }}</span>
              }
              @case ('player') {
                <span class="player-message" [class.own-player-message]="'player' in messageNode ? messageNode.player.name === ownName() : null">{{ messageNode.text }}</span>
              }
              @case ('item') {
                <span class="item-message"
                      [class.progression]="'item' in messageNode ? messageNode.item.progression : null"
                      [class.filler]="'item' in messageNode ? messageNode.item.filler : null"
                      [class.useful]="'item' in messageNode ? messageNode.item.useful : null"
                      [class.trap]="'item' in messageNode ? messageNode.item.trap : null">{{ messageNode.text }}</span>
              }
              @case ('location') {
                <span class="location-message">{{ messageNode.text }}</span>
              }
              @case ('color') {
                <!--
                  not really correct, but technically the only color nodes the server returns is "green" or "red"
                  so it's fine enough for an example.
                -->
                <span [style.color]="'color' in messageNode ? messageNode.color : null">{{ messageNode.text }}</span>
              }
              @default {
                <span>{{ messageNode.text }}</span>
              }
            }
          }
        </p>
      }

      <form class="message-send-form" (submit)="onSend($event)">
        <input #txt class="message-send-box" type="text" [value]="messageToSend()" (input)="messageToSend.set(txt.value)" />
        <input class="message-send-button" type="submit" value="Send" [disabled]="sendingMessage()" />
      </form>
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
      white-space: pre;
    }

    .player-message {
      font-weight: bold;
      color: #FAFAD2;
      &.own-player-message {
        color: #EE00EE;
      }
    }

    .location-message {
      color: #00FF7F;
    }

    .entrance-message {
      color: #6495ED;
    }

    .item-message {
      font-weight: bold;
      &.filler {
        color: rgb(6, 217, 217);
      }
      &.progression {
        color: rgb(168, 147, 228);
      }
      &.useful {
        color: rgb(98, 122, 198);
      }
      &.progression.useful {
        color: rgb(255, 223, 0);
      }
      &.trap {
        color: rgb(211, 113, 102);
      }
      &.progression.trap {
        color: rgb(255, 172, 28);
      }
      &.useful.trap {
        color: rgb(155, 89, 182);
      }
      &.progression.useful.trap {
        color: rgb(128, 255, 128);
      }
    }

    .message-send-form {
      flex: 0;
      width: 100%;
      display: flex;
    }

    .message-send-box {
      flex: 1;
    }

    .message-send-button {
      flex: 0;
      margin-left: 5px;
    }
  `,
})
export class GameTabTextClient {
  readonly game = input.required<AutopelagoClientAndData>();
  readonly ownName = signal<string>('');
  readonly dateFormatter = new Intl.DateTimeFormat(navigator.languages[0], { dateStyle: 'short', timeStyle: 'medium' });
  readonly messageToSend = signal('');
  readonly #sendingMessage = signal(false);
  readonly sendingMessage = this.#sendingMessage.asReadonly();

  constructor() {
    effect((onCleanup) => {
      const { client } = this.game();
      this.ownName.set(client.players.self.toString());
      const onAliasUpdated = (player: Player) => {
        this.ownName.set(player.toString());
      };
      client.players.on('aliasUpdated', onAliasUpdated);
      onCleanup(() => client.players.off('aliasUpdated', onAliasUpdated));
    });
  }

  async onSend(event: SubmitEvent) {
    event.preventDefault();
    const messageToSend = this.messageToSend();
    if (!messageToSend) {
      return;
    }

    this.#sendingMessage.set(true);
    try {
      await this.game().client.messages.say(messageToSend);
      if (this.messageToSend() === messageToSend) {
        this.messageToSend.set('');
      }
    }
    finally {
      this.#sendingMessage.set(false);
    }
  }
}
