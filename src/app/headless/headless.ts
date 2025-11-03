import { Component, effect, inject, input, signal } from '@angular/core';
import type { GamePackage } from 'archipelago.js';
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

    effect(() => void this.#sendUpdates());
    effect(() => {
      console.log('rat count', this.#gameStore.ratCount());
    });
  }

  async #loadPackage() {
    const { client, packageChecksum } = this.game();
    if (packageChecksum) {
      const dataPackageStr = localStorage.getItem(packageChecksum);
      if (dataPackageStr) {
        try {
          client.package.importPackage({
            games: {
              Autopelago: JSON.parse(dataPackageStr) as GamePackage,
            },
          });
        }
        catch (e) {
          localStorage.removeItem(packageChecksum);
          console.error('error loading package', e);
        }
      }
    }

    if (client.package.findPackage('Autopelago')) {
      return;
    }

    const pkg = await client.package.fetchPackage(['Autopelago']);
    if ('Autopelago' in pkg.games) {
      const autopelagoPkg = pkg.games['Autopelago'];
      localStorage.setItem(autopelagoPkg.checksum, JSON.stringify(autopelagoPkg));
    }
  }

  doTickStuff() {
    // empty
  }

  readonly #initState = signal(0);
  #prevSendUpdates: Promise<unknown> = Promise.resolve();

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

  async #sendUpdates() {
    const { client, slotData, storedData, storedDataKey } = this.game();
    const itemsJustReceived: number[] = [];
    switch (this.#initState()) {
      case 0: {
        this.#initState.set(1);
        await this.#loadPackage();
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
        this.#initState.set(2);
        break;
      }

      case 1:
        break;

      case 2: {
        this.#initState.set(1);
        client.items.on('itemsReceived', (items) => {
          itemsJustReceived.length = 0;
          for (const item of items) {
            const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
            if (typeof itemKey === 'number') {
              itemsJustReceived.push(itemKey);
            }
          }

          this.#initState.set(3);
          this.#gameStore.receiveItems(itemsJustReceived);
        });

        client.room.on('locationsChecked', (locations) => {
          this.#gameStore.checkLocations(this.#mapLocations(locations));
        });
        break;
      }

      case 3: {
        const newStoredData = this.#gameStore.asStoredData();
        const prevSendUpdates = this.#prevSendUpdates;
        await prevSendUpdates;
        if (prevSendUpdates !== this.#prevSendUpdates) {
          return;
        }

        console.log('sending updates', newStoredData);
        this.#prevSendUpdates = client.storage
          .prepare(storedDataKey, newStoredData)
          .replace(newStoredData)
          .commit(true);
        break;
      }
    }
  }
}
