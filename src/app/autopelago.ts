import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';

import { ArchipelagoClient } from './archipelago-client';
import { GameStore } from './store/autopelago-store';
import { ConnectScreenStore } from './store/connect-screen.store';

@Injectable({
  providedIn: 'root',
})
export class AutopelagoService {
  readonly #connectScreenStore = inject(ConnectScreenStore);
  readonly #store = inject(GameStore);
  readonly #ap = inject(ArchipelagoClient);
  readonly #router = inject(Router);

  async connect() {
    this.disconnect();

    try {
      await this.#ap.connect({
        directHost: this.#connectScreenStore.directHost(),
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
    }
  }

  async say(message: string) {
    return this.#ap.say(message);
  }

  disconnect() {
    this.#ap.disconnect();
  }
}
