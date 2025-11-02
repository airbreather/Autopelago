import { withImmutableState, withStorageSync } from '@angular-architects/ngrx-toolkit';
import { computed } from '@angular/core';

import { patchState, signalStore, withComputed, withMethods } from '@ngrx/signals';

import { type MessageNode } from 'archipelago.js';
import { List, Set } from 'immutable';
import { BAKED_DEFINITIONS_FULL, type VictoryLocationYamlKey } from '../data/resolved-definitions';
import type { AutopelagoStoredData } from '../data/slot-data';

export interface Message {
  ts: Date;
  originalNodes: readonly Readonly<MessageNode>[];
}

const initialState = {
  lactoseIntolerant: false,
  victoryLocationYamlKey: null as VictoryLocationYamlKey | null,
  paused: false,
  messages: [] as readonly Message[],
  foodFactor: 0,
  luckFactor: 0,
  energyFactor: 0,
  styleFactor: 0,
  distractionCounter: 0,
  startledCounter: 0,
  hasConfidence: false,
  mercyFactor: 0,
  sluggishCarryover: false,
  processedReceivedItemCount: 0,
  currentLocation: null as number | null,
  priorityPriorityLocations: List<number>(),
  priorityLocations: List<number>(),
  receivedItems: List<number>(),
  receivedItemCountLookup: List<number>(Array<number>(BAKED_DEFINITIONS_FULL.allItems.length).fill(0)),
  checkedLocations: Set<number>(),
};

// Local storage key
const STORAGE_KEY = 'autopelago-game-state';

export const GameStore = signalStore(
  withImmutableState(initialState),
  withStorageSync({
    key: STORAGE_KEY,
    select: ({ paused }) => ({ paused }),
  }),
  withComputed(store => ({
    ratCount: computed<number>(() => {
      let ratCount = 0;
      const lookup = store.receivedItemCountLookup();
      for (const item of BAKED_DEFINITIONS_FULL.itemsWithNonzeroRatCounts) {
        const thisItemCount = lookup.get(item);
        if (thisItemCount) {
          ratCount += thisItemCount * BAKED_DEFINITIONS_FULL.allItems[item].ratCount;
        }
      }
      return ratCount;
    }),
    asStoredData: computed<AutopelagoStoredData>(() => {
      const currentLocation = store.currentLocation();
      if (currentLocation === null) {
        throw new Error('Must initialize first');
      }

      return {
        foodFactor: store.foodFactor(),
        luckFactor: store.luckFactor(),
        energyFactor: store.energyFactor(),
        styleFactor: store.styleFactor(),
        distractionCounter: store.distractionCounter(),
        startledCounter: store.startledCounter(),
        hasConfidence: store.hasConfidence(),
        mercyFactor: store.mercyFactor(),
        sluggishCarryover: store.sluggishCarryover(),
        processedReceivedItemCount: store.processedReceivedItemCount(),
        currentLocation,
        priorityPriorityLocations: store.priorityPriorityLocations().toJS(),
        priorityLocations: store.priorityLocations().toJS(),
      };
    }),
  })),
  withMethods(store => ({
    appendMessage(message: Readonly<Message>) {
      patchState(store, ({ messages }) => ({ messages: [...messages, message] }));
    },
    pause() {
      patchState(store, { paused: true });
    },
    unpause() {
      patchState(store, { paused: false });
    },
    togglePause() {
      patchState(store, ({ paused }) => ({ paused: !paused }));
    },
    moveTo(currentLocation: number) {
      patchState(store, { currentLocation });
    },
    initFromServer(storedData: Partial<AutopelagoStoredData>, victoryLocationYamlKey: VictoryLocationYamlKey) {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const patchData: any = { ...storedData, victoryLocationYamlKey };
      if ('priorityPriorityLocations' in storedData) {
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
        patchData.priorityPriorityLocations = List(storedData.priorityPriorityLocations);
      }
      if ('priorityLocations' in storedData) {
        // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
        patchData.priorityLocations = List(storedData.priorityLocations);
      }
      // eslint-disable-next-line @typescript-eslint/no-unsafe-argument
      patchState(store, patchData);
    },
    receiveItems(items: Iterable<number>) {
      const lactoseIntolerant = store.lactoseIntolerant();
      patchState(store, (prev) => {
        const result = {
          foodFactor: prev.foodFactor,
          luckFactor: prev.luckFactor,
          energyFactor: prev.energyFactor,
          styleFactor: prev.styleFactor,
          distractionCounter: prev.distractionCounter,
          startledCounter: prev.startledCounter,
          hasConfidence: prev.hasConfidence,
          // the remainder will be clobbered. just helping TypeScript.
          receivedItems: prev.receivedItems,
          receivedItemCountLookup: prev.receivedItemCountLookup,
          processedReceivedItemCount: prev.processedReceivedItemCount,
        } satisfies Partial<typeof initialState>;
        result.receivedItemCountLookup = result.receivedItemCountLookup.withMutations((l) => {
          result.receivedItems = prev.receivedItems.withMutations((r) => {
            for (const item of items) {
              const itemFull = BAKED_DEFINITIONS_FULL.allItems[item];
              r.push(item);
              l.update(item, 0, i => i + 1);
              if (r.size <= prev.processedReceivedItemCount) {
                console.log('received previously known item', lactoseIntolerant ? itemFull.lactoseIntolerantName : itemFull.lactoseName);
                continue;
              }
              else {
                console.log('received new item', lactoseIntolerant ? itemFull.lactoseIntolerantName : itemFull.lactoseName);
              }

              for (const aura of itemFull.aurasGranted) {
                switch (aura) {
                  case 'well_fed':
                    result.foodFactor += 5;
                    break;

                  case 'upset_tummy':
                    result.foodFactor -= 5;
                    break;

                  // and so on for other auras...
                }
              }
            }
          });
        });

        result.processedReceivedItemCount = result.receivedItems.size;
        return result;
      });
    },
  })),
);
