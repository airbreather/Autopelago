import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';

import { ArchipelagoClient } from './archipelago-client';
import { ConnectScreenStoreService } from './store/connect-screen.store';

@Injectable({
  providedIn: 'root',
})
export class AutopelagoService {
  readonly #connectScreenStore = inject(ConnectScreenStoreService);
  readonly #ap = inject(ArchipelagoClient);
  readonly #router = inject(Router);

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
    }
  }

  async say(message: string) {
    return this.#ap.say(message);
  }

  disconnect() {
    this.#ap.disconnect();
  }
}
