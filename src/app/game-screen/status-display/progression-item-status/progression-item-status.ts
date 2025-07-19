import { NgClass } from '@angular/common';
import { Component, computed, signal } from '@angular/core';

import { PROGRESSION_ITEMS_TO_DISPLAY } from '../../../data/items';

function createItem(name: string, index: number) {
  const collected = signal(index % 4 === 0);
  return {
    name,
    collected,
    offsetX: computed(() => collected() ? 0 : 65),
    offsetY: index * 65,
  };
}

@Component({
  selector: 'app-progression-item-status',
  imports: [
    NgClass,
  ],
  template: `
    <div class="outer">
      @for (item of items; track item.name) {
        <div class="item-container" [ngClass]="{ collected: item.collected() }">
          <img class="item"
               src="/assets/images/items.webp"
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
  readonly items = PROGRESSION_ITEMS_TO_DISPLAY.map(createItem);
}
