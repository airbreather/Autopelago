import { Component, effect, inject, input } from '@angular/core';
import { Ticker } from 'pixi.js';

import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
} from '../data/resolved-definitions';

import type { AutopelagoClientAndData } from '../data/slot-data';

import { GameStore } from '../store/autopelago-store';

const TICK_INTERVAL_MIN = 1000;
const TICK_INTERVAL_MAX = 20000;

interface PauseUnpause {
  pause(): void;

  unpause(): void;
}

function handleTick(this: Headless, ticker: Ticker) {
  this.remaining -= ticker.deltaMS;
  while (this.remaining < 0) {
    console.log('tick!');
    this.doTickStuff();
    this.remaining += Math.floor(Math.random() * (TICK_INTERVAL_MAX - TICK_INTERVAL_MIN) + TICK_INTERVAL_MIN);
  }
}

@Component({
  selector: 'app-headless',
  imports: [],
  template: `
    <p>
      check your browser console, bro!
    </p>
  `,
  styles: '',
})
export class Headless {
  readonly #gameStore = inject(GameStore);
  protected readonly game = input.required<AutopelagoClientAndData>();
  remaining = Math.floor(Math.random() * (TICK_INTERVAL_MAX - TICK_INTERVAL_MIN) + TICK_INTERVAL_MIN);

  constructor() {
    const pauseUnpause = globalThis as unknown as PauseUnpause;
    pauseUnpause.pause = () => {
      Ticker.shared.stop();
    };
    pauseUnpause.unpause = () => {
      Ticker.shared.start();
    };
    Ticker.shared.add(handleTick, this);

    const initEffect = effect(() => {
      this.#init();
      initEffect.destroy();
    });
    effect(() => {
      this.#sendUpdates();
    });
    effect(() => {
      console.log('rat count', this.#gameStore.ratCount());
    });
  }

  doTickStuff() {
    // empty
  }

  #mapLocations(locations: Iterable<number>): ReadonlySet<number> {
    const { client, slotData } = this.game();
    const victoryLocationName = slotData.victory_location_name;
    const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[victoryLocationName];
    const defs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
    const result = new Set<number>();
    for (const location of locations) {
      const locationName = client.package.lookupLocationName('Autopelago', location, false);
      if (!locationName) {
        continue;
      }

      const locationId = defs.locationNameLookup.get(locationName);
      if (typeof locationId === 'number') {
        result.add(locationId);
      }
    }

    return result;
  }

  #init() {
    const { client, slotData, storedData } = this.game();
    const itemsJustReceived: number[] = [];
    const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];
    const locations = this.#mapLocations(client.room.checkedLocations);
    this.#gameStore.initFromServer(storedData, locations, slotData.lactose_intolerant, victoryLocationYamlKey);
    for (const item of client.items.received) {
      const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
      if (typeof itemKey === 'number') {
        itemsJustReceived.push(itemKey);
      }
    }

    this.#gameStore.receiveItems(itemsJustReceived);
    client.items.on('itemsReceived', (items) => {
      itemsJustReceived.length = 0;
      for (const item of items) {
        const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
        if (typeof itemKey === 'number') {
          itemsJustReceived.push(itemKey);
        }
      }

      this.#gameStore.receiveItems(itemsJustReceived);
    });

    client.room.on('locationsChecked', (locations) => {
      this.#gameStore.checkLocations(this.#mapLocations(locations));
    });
  }

  #prevSendUpdates: Promise<void> | null = null;
  #sendUpdates() {
    const { client, storedDataKey } = this.game();
    const newStoredData = this.#gameStore.asStoredData();
    const prevSendUpdates = this.#prevSendUpdates;
    if (!prevSendUpdates) {
      // the first time through, the value (by definition) hasn't changed from the initial state, so
      // there's no need to send this redundant update.
      this.#prevSendUpdates = Promise.resolve();
      return;
    }

    this.#prevSendUpdates = (async () => {
      await prevSendUpdates;
      if (this.#prevSendUpdates !== prevSendUpdates) {
        // another update was queued while we were waiting for this one to finish. this means that
        // there's a newer promise (a)waiting on us, so we can just return.
        return;
      }

      await client.storage
        .prepare(storedDataKey, newStoredData)
        .replace(newStoredData)
        .commit(true);
    })();
  }
}
