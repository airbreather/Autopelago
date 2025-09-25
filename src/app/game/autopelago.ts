import { computed, effect, inject, Injectable, type Signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Seq } from 'immutable';

import { type ConnectedPacket, Item } from 'archipelago.js';

import { ArchipelagoClient, type ConnectOptions } from '../archipelago-client';
import type { AutopelagoBuff, AutopelagoTrap } from '../data/definitions-file';
import type { LandmarkName } from '../data/locations';
import type { AutopelagoDefinitions } from '../data/resolved-definitions';
import { GameStore } from '../store/autopelago-store';
import { GameDefinitionsStore } from '../store/game-definitions-store';
import { strictObjectEntries } from '../util';

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

interface GameState {
  itemByDataId: ReadonlyMap<number, number>;
  locationByDataId: ReadonlyMap<number, number>;
  dataIdByItem: readonly number[];
  dataIdByLocation: readonly number[];
  resolvedDefs: Readonly<AutopelagoDefinitions>;
}

@Injectable()
export class AutopelagoService {
  readonly rawClient = new ArchipelagoClient();
  readonly #store = inject(GameStore);
  readonly #gameState: Signal<GameState | null>;

  constructor() {
    const gameDefinitionsStore = inject(GameDefinitionsStore);
    effect(() => {
      this.#store.setDefs(gameDefinitionsStore.resolvedDefs());
    });
    this.#gameState = computed(() => {
      const res = this.#store.defs();
      if (!res) {
        return null;
      }

      const gamePackage = this.rawClient.gamePackage.value();
      if (!gamePackage) {
        return null;
      }

      const itemByName = new Map(res.allItems.map((item, i) => [item.name, i]));
      const locationByName = new Map(res.allLocations.map((location, i) => [location.name, i]));

      const itemByDataId = new Map<number, number>();
      const dataIdByItem = new Array<number>(res.allItems.length);
      for (const [name, dataId] of strictObjectEntries(gamePackage.item_name_to_id)) {
        const item = itemByName.get(name);
        if (item === undefined) {
          // don't throw, this could just be lactose vs. intolerant
          continue;
        }

        itemByDataId.set(dataId, item);
        dataIdByItem[item] = dataId;
      }

      const locationByDataId = new Map<number, number>();
      const dataIdByLocation = new Array<number>(res.allLocations.length);
      for (const [name, dataId] of strictObjectEntries(gamePackage.location_name_to_id)) {
        const location = locationByName.get(name);
        if (location === undefined) {
          throw new Error(`did not recognize location ${name}`);
        }

        locationByDataId.set(dataId, location);
        dataIdByLocation[location] = dataId;
      }

      return {
        itemByDataId,
        locationByDataId,
        dataIdByItem,
        dataIdByLocation,
        resolvedDefs: res,
      };
    });

    this.rawClient.events('socket', 'connected').pipe(
      takeUntilDestroyed(),
    ).subscribe(([packet]) => {
      // noinspection JSUnusedLocalSymbols
      const _slotData = (packet as unknown as AutopelagoConnectedPacket).slot_data;
    });

    this.rawClient.events('messages', 'message')
      .pipe(takeUntilDestroyed())
      .subscribe((msg) => {
        this.#store.appendMessage({ ts: new Date(), originalNodes: msg[1] });
      });

    this.rawClient.events('items', 'itemsReceived')
      .pipe(takeUntilDestroyed())
      .subscribe(([items]) => {
        this.#onReceivedItems(items);
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

  #onReceivedItems(items: Iterable<Item>) {
    const gameState = this.#gameState();
    if (!gameState) {
      return;
    }

    const { itemByDataId } = gameState;
    this.#store.receiveItems(Seq(items)
      .map(i => itemByDataId.get(i.id))
      .filter(i => i !== undefined));
  }
}
