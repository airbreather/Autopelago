import { inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import type { ConnectedPacket } from 'archipelago.js';

import { ArchipelagoClient, type ConnectOptions } from '../archipelago-client';
import type { AutopelagoBuff, AutopelagoTrap } from '../data/definitions-file';
import type { LandmarkName } from '../data/locations';
import { GameStore } from '../store/autopelago-store';

type AutopelagoWeightedMessage = readonly [string, number];

interface AutopelagoSlotData {
  version_stamp: string;
  victory_location_name: LandmarkName;
  enabled_buffs: readonly AutopelagoBuff[];
  enabled_traps: readonly AutopelagoTrap[];
  msg_changed_target: readonly AutopelagoWeightedMessage[];
  msg_enter_go_mode: readonly AutopelagoWeightedMessage[];
  msg_enter_bk: readonly AutopelagoWeightedMessage[];
  msg_remind_bk: readonly AutopelagoWeightedMessage[];
  msg_exit_bk: readonly AutopelagoWeightedMessage[];
  msg_completed_goal: readonly AutopelagoWeightedMessage[];
  lactose_intolerant: boolean;
}

type AutopelagoConnectedPacket = ConnectedPacket & {
  readonly slot_data: Readonly<AutopelagoSlotData>;
};

@Injectable()
export class AutopelagoService {
  readonly rawClient = new ArchipelagoClient();

  readonly #gameStore = inject(GameStore);

  constructor() {
    this.rawClient.events('socket', 'connected').pipe(
      takeUntilDestroyed(),
    ).subscribe(([packet]) => {
      // noinspection JSUnusedLocalSymbols
      const _slotData = (packet as unknown as AutopelagoConnectedPacket).slot_data;
    });

    this.rawClient.events('messages', 'message')
      .pipe(takeUntilDestroyed())
      .subscribe((msg) => {
        this.#gameStore.appendMessage({ ts: new Date(), originalNodes: msg[1] });
      });
  }

  async connect(options: ConnectOptions): Promise<void> {
    await this.rawClient.connect(options);
  }

  async say(message: string) {
    return this.rawClient.say(message);
  }

  disconnect() {
    this.rawClient.disconnect();
  }
}
