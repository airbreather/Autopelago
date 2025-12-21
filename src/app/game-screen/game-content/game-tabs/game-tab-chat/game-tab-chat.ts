import { Player } from '@airbreather/archipelago.js';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { GameStore } from '../../../../store/autopelago-store';

@Component({
  selector: 'app-game-tab-chat',
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="outer">
      <div class="filler"></div>
      <div class="messages">
        @for (message of messages(); track $index) {
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
                  <span class="important-message-part player-text" [class.own-player-text]="isSelf(messageNode.player)">{{ messageNode.text }}</span>
                }
                @case ('item') {
                  <span class="important-message-part item-text"
                        [class.progression]="messageNode.item.progression" [class.filler]="messageNode.item.filler"
                        [class.useful]="messageNode.item.useful" [class.trap]="messageNode.item.trap">{{ messageNode.text }}</span>
                }
                @case ('location') {
                  <span class="location-text">{{ messageNode.text }}</span>
                }
                @case ('color') {
                  <!--
                    not really correct, but technically the only color nodes the server returns is "green" or "red"
                    so it's fine enough for an example.
                  -->
                  <span [style.color]="messageNode.color">{{ messageNode.text }}</span>
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
  readonly #store = inject(GameStore);
  protected readonly messages = computed(() => this.#store.game()?.messageLog().reverse() ?? []);

  readonly dateFormatter = new Intl.DateTimeFormat(navigator.languages, { dateStyle: 'short', timeStyle: 'medium' });
  protected readonly messageToSend = signal('');
  readonly #sendingMessage = signal(false);
  protected readonly sendingMessage = this.#sendingMessage.asReadonly();

  protected async onSend(event: SubmitEvent) {
    event.preventDefault();
    const messageToSend = this.messageToSend();
    if (!messageToSend) {
      return;
    }

    const game = this.#store.game();
    if (game === null) {
      return;
    }

    this.#sendingMessage.set(true);
    try {
      await game.client.messages.say(messageToSend);
      if (this.messageToSend() === messageToSend) {
        this.messageToSend.set('');
      }
    }
    finally {
      this.#sendingMessage.set(false);
    }
  }

  protected isSelf(player: Player) {
    const game = this.#store.game();
    if (game === null) {
      return false;
    }

    const { team, slot } = game.client.players.self;
    return player.team === team && player.slot === slot;
  }
}
