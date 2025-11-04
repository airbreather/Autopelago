import { Component, computed, effect, ElementRef, inject, input, viewChild } from '@angular/core';
import { rxResource, takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';

import { SplitAreaComponent, SplitComponent } from 'angular-split';

import { map, mergeMap } from 'rxjs';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
} from '../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../data/slot-data';
import { GameStore } from '../store/autopelago-store';

import { GameScreenStore } from '../store/game-screen-store';
import { resizeEvents } from '../util';
import { GameTabs } from './game-tabs/game-tabs';
import { StatusDisplay } from './status-display/status-display';

@Component({
  selector: 'app-game-screen',
  imports: [
    SplitComponent, SplitAreaComponent, StatusDisplay, GameTabs,
  ],
  template: `
    <div #outer class="outer">
      <as-split #split unit="pixel" direction="horizontal"
                gutterDblClickDuration="500" (gutterDblClick)="onGutterDblClick()">
        <as-split-area class="left" [size]="leftSize()" [minSize]="minSize()" [maxSize]="maxSize()">
          <app-status-display [game]="game()" />
        </as-split-area>
        <as-split-area class="right">
          <app-game-tabs [game]="game()" />
        </as-split-area>
      </as-split>
    </div>
  `,
  styles: `
    .outer {
      width: 100vw;
      height: 100vh;
    }
  `,
})
export class GameScreen {
  readonly #store = inject(GameScreenStore);
  readonly #gameStore = inject(GameStore);
  readonly game = input.required<AutopelagoClientAndData>();
  protected readonly splitRef = viewChild.required<SplitComponent>('split');
  protected readonly outerRef = viewChild.required<ElementRef<HTMLDivElement>>('outer');

  readonly #width = rxResource<number | null, ElementRef<HTMLDivElement>>({
    defaultValue: null,
    params: () => this.outerRef(),
    stream: ({ params: outerRef }) => {
      return resizeEvents(outerRef.nativeElement).pipe(
        map(() => outerRef.nativeElement.scrollWidth),
      );
    },
  });

  readonly minSize = computed(() => {
    const width = this.#width.value();
    if (!width) {
      return 100;
    }

    return Math.min(100, width - 40);
  });

  readonly maxSize = computed(() => {
    const width = this.#width.value();
    if (!width) {
      return Number.MAX_SAFE_INTEGER;
    }

    return width - 40;
  });

  readonly leftSize = computed(() => {
    let val = this.#store.leftSize();
    if (!val) {
      const width = this.#width.value();
      if (width) {
        val = width * 0.2;
      }
      else {
        val = this.minSize();
      }
    }

    return Math.min(Math.max(val, this.minSize()), this.maxSize());
  });

  constructor() {
    toObservable(this.splitRef).pipe(
      mergeMap(split => split.dragProgress$),
      takeUntilDestroyed(),
    ).subscribe((evt) => {
      this.#store.updateLeftSize(evt.sizes[0] as number);
    });

    const initEffect = effect(() => {
      this.#init();
      initEffect.destroy();
    });
    effect(() => {
      this.#sendUpdates();
    });
  }

  onGutterDblClick() {
    const width = this.#width.value();
    if (width) {
      this.#store.updateLeftSize(width * 0.2);
    }
  }

  #init() {
    const { client, slotData, storedData } = this.game();
    const itemsJustReceived: number[] = [];
    const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];
    const pkg = client.package.findPackage('Autopelago');
    if (!pkg) {
      throw new Error('could not find Autopelago package');
    }

    const locationNameLookup = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].locationNameLookup;
    this.#gameStore.initFromServer(storedData, client.room.checkedLocations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1), slotData.lactose_intolerant, victoryLocationYamlKey);
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
