import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  viewChild,
} from '@angular/core';

import { RouterLink } from '@angular/router';

import { ConnectScreenStoreService } from '../../../store/connect-screen.store';
import { resizeText } from '../../../util';

@Component({
  selector: 'app-player-name-and-navigation',
  imports: [
    RouterLink,
  ],
  template: `
    <div #outer class="outer">
      <div #playerNameDiv class="player-name">{{ playerName() }}</div>
      <div><button #returnButton class="return-button" routerLink="/">Back to Main Menu</button></div>
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

    .return-button {
      font-size: 30px;
      text-wrap: nowrap;
    }
  `,
})
export class PlayerNameAndNavigation implements AfterViewInit {
  readonly #connectScreenStore = inject(ConnectScreenStoreService);
  readonly #destroy = inject(DestroyRef);
  readonly #injector = inject(Injector);

  readonly playerName = this.#connectScreenStore.slot;

  readonly outerElement = viewChild.required<ElementRef<HTMLElement>>('outer');
  readonly playerNameElement = viewChild.required<ElementRef<HTMLElement>>('playerNameDiv');
  readonly returnButtonElement = viewChild.required<ElementRef<HTMLElement>>('returnButton');

  ngAfterViewInit(): void {
    resizeText({ outer: this.outerElement, inner: this.playerNameElement, max: 50, destroy: this.#destroy, injector: this.#injector });
    resizeText({ outer: this.outerElement, inner: this.returnButtonElement, max: 30, destroy: this.#destroy, injector: this.#injector });
  }
}
