import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  input,
  signal,
  viewChild,
} from '@angular/core';

import { RouterLink } from '@angular/router';
import type { Player } from 'archipelago.js';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';

import { resizeText } from '../../../../resize-text';

@Component({
  selector: 'app-player-name-and-navigation',
  imports: [
    RouterLink,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #outer class="outer">
      <div #playerNameDiv class="player-name">{{ playerName() }}</div>
      <div>
        <button #returnButton class="return-button" routerLink="/">Back to Main Menu</button>
      </div>
    </div>
  `,
  styles: `
    .outer {
      text-align: start;
      margin-left: 5px;
      margin-right: 5px;
    }

    .player-name {
      font-size: 50px;
    }
  `,
})
export class PlayerNameAndNavigation {
  readonly game = input.required<AutopelagoClientAndData>();

  readonly #player = signal({ name: '', alias: '' });
  readonly playerName = computed(() => {
    const { name, alias } = this.#player();
    return new RegExp(`(?<justAlias>.*)\\(${name}\\)`).exec(alias)?.groups?.['justAlias'] ?? alias;
  });

  readonly outerElement = viewChild.required<ElementRef<HTMLElement>>('outer');
  readonly playerNameElement = viewChild.required<ElementRef<HTMLElement>>('playerNameDiv');
  readonly returnButtonElement = viewChild.required<ElementRef<HTMLElement>>('returnButton');

  constructor() {
    resizeText({ outer: this.outerElement, inner: this.playerNameElement, max: 50 });
    resizeText({ outer: this.outerElement, inner: this.returnButtonElement, max: 30 });

    effect((onCleanup) => {
      const { client } = this.game();
      this.#player.set({ name: client.players.self.name, alias: client.players.self.alias });

      const cb = (player: Player, _oldAlias: string, newAlias: string) => {
        if (player.slot === client.players.self.slot) {
          this.#player.set({ name: client.players.self.name, alias: newAlias });
        }
      };
      client.players.on('aliasUpdated', cb);
      onCleanup(() => client.players.off('aliasUpdated', cb));
    });
  }
}
