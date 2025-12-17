import { ChangeDetectionStrategy, Component, effect, input, signal } from '@angular/core';
import { Player } from 'archipelago.js';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';

@Component({
  selector: 'app-game-tab-chat',
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer">
      <div class="filler"></div>
      <div class="messages">
        @for (message of game().messageLog().reverse(); track $index) {
          <p class="message">
            [<time [dateTime]="message.ts.toISOString()">{{ dateFormatter.format(message.ts) }}</time>]
            @for (messageNode of message.nodes; track $index) {
              <!--
                Follow Yacht Dice:
                https://github.com/spinerak/YachtDiceAP/blob/936dde8034a88b653b71a2d653467203ea781d41/index.html#L4517-L4591
              -->
              @switch (messageNode.type) {
                @case ('entrance') {
                  <span class="entrance-text">{{ messageNode.text }}</span>
                }
                @case ('player') {
                  <span class="important-message-part player-text" [class.own-player-text]="'player' in messageNode ? messageNode.player.name === ownName() : null">{{ messageNode.text }}</span>
                }
                @case ('item') {
                  <span class="important-message-part item-text"
                        [class.progression]="'item' in messageNode ? messageNode.item.progression : null"
                        [class.filler]="'item' in messageNode ? messageNode.item.filler : null"
                        [class.useful]="'item' in messageNode ? messageNode.item.useful : null"
                        [class.trap]="'item' in messageNode ? messageNode.item.trap : null">{{ messageNode.text }}</span>
                }
                @case ('location') {
                  <span class="location-text">{{ messageNode.text }}</span>
                }
                @case ('color') {
                  <!--
                    not really correct, but technically the only color nodes the server returns is "green" or "red"
                    so it's fine enough for an example.
                  -->
                  <span [style.color]="'color' in messageNode ? messageNode.color : null">{{ messageNode.text }}</span>
                }
                @default {
                  <span [class.server-message]="message.type === 'serverChat'">{{ messageNode.text }}</span>
                }
              }
            }
          </p>
        }
      </div>

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
      width: 100%;
      --font-family: sans-serif;
    }

    .messages {
      display: flex;
      flex-direction: column-reverse;
      overflow-y: auto;
      width: 100%;
    }

    .filler {
      flex: 1;
    }

    .message {
      flex: 0;
      margin-block-start: 0;
      margin-block-end: 0;
      white-space: preserve wrap;
    }

    .server-message {
      color: #FF6060;
      font-weight: bold;
    }

    .important-message-part {
      font-weight: bold;
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
export class GameTabChat {
  readonly game = input.required<AutopelagoClientAndData>();
  protected readonly ownName = signal<string>('');
  readonly dateFormatter = new Intl.DateTimeFormat(navigator.languages, { dateStyle: 'short', timeStyle: 'medium' });
  protected readonly messageToSend = signal('');
  readonly #sendingMessage = signal(false);
  protected readonly sendingMessage = this.#sendingMessage.asReadonly();

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

  protected async onSend(event: SubmitEvent) {
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
