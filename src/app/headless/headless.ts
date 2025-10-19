import { Component, effect, inject, input } from '@angular/core';

import type { AutopelagoClientAndData } from '../data/slot-data';
import type { GamePackage } from 'archipelago.js';

import type { AutopelagoDefinitions } from '../data/resolved-definitions';
import { GameStore } from '../store/autopelago-store';

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

  constructor() {
    const eff = effect(() => {
      const defs = this.#gameStore.defs();
      if (!defs) {
        return;
      }

      void this.#setUpReceivedItemsHandling(defs);
      eff.destroy();
    });
  }

  async #setUpReceivedItemsHandling(defs: AutopelagoDefinitions) {
    const itemByName = new Map<string, number>();
    for (let i = 0; i < defs.allItems.length; i++) {
      const item = defs.allItems[i];
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
