import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';

import { ArchipelagoClient } from '../archipelago-client';
import { GameStoreService } from '../store/autopelago-store';
import { ConnectScreenStoreService } from '../store/connect-screen.store';

@Injectable({
  providedIn: 'root',
})
export class AutopelagoService {
  readonly #connectScreenStore = inject(ConnectScreenStoreService);
  readonly #ap = inject(ArchipelagoClient);
  readonly #router = inject(Router);

  // this is unused for now, but it is expected to be used later, and putting it here forces it to
  // get initialized at the correct time (otherwise we miss a few messages in the Text Client tab).
  /* eslint-disable no-unused-private-class-members */
  // noinspection JSUnusedLocalSymbols
  readonly #gameStore = inject(GameStoreService);
  /* eslint-enable no-unused-private-class-members */

  async connect() {
    this.disconnect();

    try {
      await this.#ap.connect({
        host: this.#connectScreenStore.host(),
        port: this.#connectScreenStore.port(),
        slot: this.#connectScreenStore.slot(),
        password: this.#connectScreenStore.password(),
      });
      console.log('Successfully connected to Archipelago server!');
      await this.#router.navigate(['./game']);
    }
    catch (error) {
      console.error('Failed to connect to Archipelago server:', error);
      // TODO: Show user-friendly error message
      return;
    }
  }

  async say(message: string) {
    return this.#ap.say(message);
  }

  disconnect() {
    this.#ap.disconnect();
  }
}
