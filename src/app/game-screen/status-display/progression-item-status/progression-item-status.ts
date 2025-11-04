import { NgClass } from '@angular/common';
import { Component, computed, inject, input, type Signal } from '@angular/core';
import { PROGRESSION_ITEMS_BY_VICTORY_LOCATION } from '../../../data/items';
import { BAKED_DEFINITIONS_FULL } from '../../../data/resolved-definitions';

import type { AutopelagoClientAndData } from '../../../data/slot-data';
import { GameStore } from '../../../store/autopelago-store';

@Component({
  selector: 'app-progression-item-status',
  imports: [
    NgClass,
  ],
  template: `
    <div class="outer">
      @for (item of items(); track item.name) {
        <div class="item-container" [ngClass]="{ collected: item.collected() }">
          <img class="item"
               src="assets/images/items.webp"
               [alt]="item.name"
               [style.object-position]="-item.offsetX() + 'px ' + -item.offsetY + 'px'">
        </div>
      }
    </div>
  `,
  styles: `
    .outer {
      display: flex;
      flex-wrap: wrap;
    }

    .item-container {
      margin: 5px;
      padding: 5px;

      border: 2px solid black;
      border-radius: 8px;

      &.collected {
        border: 2px solid #FFCE00;
      }
    }

    .item {
      object-fit: none;
      width: 64px;
      height: 64px;
    }
  `,
})
export class ProgressionItemStatus {
  readonly game = input.required<AutopelagoClientAndData>();
  readonly #gameStore = inject(GameStore);
  readonly items: Signal<readonly ItemModel[]>;
  constructor() {
    this.items = computed(() => {
      const victoryLocationYamlKey = this.#gameStore.victoryLocationYamlKey();
      if (!victoryLocationYamlKey) {
        return [];
      }

      const lactoseIntolerant = this.#gameStore.lactoseIntolerant();
      return PROGRESSION_ITEMS_BY_VICTORY_LOCATION[victoryLocationYamlKey].map((itemYamlKey, index) => {
        const item = BAKED_DEFINITIONS_FULL.progressionItemsByYamlKey.get(itemYamlKey) ?? -1;
        const collected = computed(() => this.#gameStore.receivedItemCountLookup().get(item, 0) > 0);
        return {
          name: lactoseIntolerant
            ? BAKED_DEFINITIONS_FULL.allItems[item].lactoseIntolerantName
            : BAKED_DEFINITIONS_FULL.allItems[item].lactoseName,
          collected,
          offsetX: computed(() => collected() ? 0 : 65),
          offsetY: index * 65,
        };
      });
    });
  }
}

interface ItemModel {
  name: string;
  collected: Signal<boolean>;
  offsetX: Signal<number>;
  offsetY: number;
}
