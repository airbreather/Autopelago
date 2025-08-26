import { inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';

import { ConnectedPacket } from 'archipelago.js';

import { ArchipelagoClient } from '../archipelago-client';
import { AutopelagoBuff, AutopelagoTrap } from '../data/definitions-file';
import { LandmarkName } from '../data/locations';
import { GameStore } from '../store/autopelago-store';
import { ConnectScreenStore } from '../store/connect-screen.store';

interface AutopelagoSlotData {
  version_stamp: string;
  victory_location_name: LandmarkName;
  enabled_buffs: readonly AutopelagoBuff[];
  enabled_traps: readonly AutopelagoTrap[];
  msg_changed_target: readonly string[];
  msg_enter_go_mode: readonly string[];
  msg_enter_bk: readonly string[];
  msg_remind_bk: readonly string[];
  msg_exit_bk: readonly string[];
  msg_completed_goal: readonly string[];
  lactose_intolerant: boolean;
}

type AutopelagoConnectedPacket = ConnectedPacket & {
  readonly slot_data: Readonly<AutopelagoSlotData>;
};

@Injectable({
  providedIn: 'root',
})
export class AutopelagoService {
  readonly #connectScreenStore = inject(ConnectScreenStore);
  readonly #ap = inject(ArchipelagoClient);
  readonly #router = inject(Router);

  // this is unused for now, but it is expected to be used later, and putting it here forces it to
  // get initialized at the correct time (otherwise we miss a few messages in the Text Client tab).
  /* eslint-disable no-unused-private-class-members */
  // noinspection JSUnusedLocalSymbols
  readonly #gameStore = inject(GameStore);
  /* eslint-enable no-unused-private-class-members */

  constructor() {
    this.#ap.events('socket', 'connected').pipe(
      takeUntilDestroyed(),
    ).subscribe(([packet]) => {
      // noinspection JSUnusedLocalSymbols
      const _slotData = (packet as unknown as AutopelagoConnectedPacket).slot_data;
    });
  }

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
