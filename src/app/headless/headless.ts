import { Component, effect, inject, input } from '@angular/core';
import type { GamePackage } from 'archipelago.js';
import { Ticker } from 'pixi.js';

import { BAKED_DEFINITIONS } from '../data/resolved-definitions';

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
    effect(() => void this.#setUpReceivedItemsHandling());
  }

  async #setUpReceivedItemsHandling() {
    const itemByName = new Map<string, number>();
    for (let i = 0; i < BAKED_DEFINITIONS.allItems.length; i++) {
      const item = BAKED_DEFINITIONS.allItems[i];
      itemByName.set(item.lactoseName, i);
      if (item.lactoseName !== item.lactoseIntolerantName) {
        itemByName.set(item.lactoseIntolerantName, i);
      }
    }

    const { client } = this.game();
    await this.#loadPackage();
    const itemsJustReceived: number[] = [];
    for (const item of client.items.received) {
      const itemKey = itemByName.get(item.name);
      if (typeof itemKey === 'number') {
        itemsJustReceived.push(itemKey);
      }
    }

    this.#gameStore.receiveItems(itemsJustReceived);
    client.items.on('itemsReceived', (items) => {
      itemsJustReceived.length = 0;
      for (const item of items) {
        const itemKey = itemByName.get(item.name);
        if (typeof itemKey === 'number') {
          itemsJustReceived.push(itemKey);
        }
      }

      this.#gameStore.receiveItems(itemsJustReceived);
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
}
