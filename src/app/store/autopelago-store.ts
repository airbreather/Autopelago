import { withImmutableState, withStorageSync } from '@angular-architects/ngrx-toolkit';
import { computed } from '@angular/core';

import { patchState, signalStore, withComputed, withMethods } from '@ngrx/signals';
import { List, Set } from 'immutable';
import { BAKED_DEFINITIONS_FULL, type VictoryLocationYamlKey } from '../data/resolved-definitions';
import type { AutopelagoStoredData } from '../data/slot-data';
import {
  type PreviousLocationEvidence,
  previousLocationEvidenceFromJSONSerializable,
  previousLocationEvidenceToJSONSerializable,
} from '../game/previous-location-evidence';

const initialState = {
  lactoseIntolerant: false,
  victoryLocationYamlKey: null as VictoryLocationYamlKey | null,
  paused: false,
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
  currentLocation: -1,
  previousLocationEvidence: null as PreviousLocationEvidence,
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
    asStoredData: computed<AutopelagoStoredData>(() => ({
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
      currentLocation: store.currentLocation(),
      previousLocationEvidence: previousLocationEvidenceToJSONSerializable(store.previousLocationEvidence()),
      priorityPriorityLocations: store.priorityPriorityLocations().toJS(),
      priorityLocations: store.priorityLocations().toJS(),
    })),
  })),
  withMethods(store => ({
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
    initFromServer(storedData: AutopelagoStoredData, checkedLocations: Iterable<number>, lactoseIntolerant: boolean, victoryLocationYamlKey: VictoryLocationYamlKey) {
      const patchData = {
        ...storedData,
        lactoseIntolerant,
        victoryLocationYamlKey,
        priorityLocations: List(storedData.priorityLocations),
        priorityPriorityLocations: List(storedData.priorityPriorityLocations),
        receivedItems: List<number>(),
        receivedItemCountLookup: List<number>(Array<number>(BAKED_DEFINITIONS_FULL.allItems.length).fill(0)),
        checkedLocations: Set<number>(checkedLocations),
        previousLocationEvidence: previousLocationEvidenceFromJSONSerializable(storedData.previousLocationEvidence),
      };
      patchState(store, patchData);
    },
    checkLocations(locations: Iterable<number>) {
      const locationsArray = [...locations];
      console.log('marking locations checked:', locationsArray);
      patchState(store, ({ checkedLocations }) => ({ checkedLocations: checkedLocations.union(locationsArray) }));
    },
    receiveItems(items: Iterable<number>) {
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
              let subtractConfidence = false;
              let addConfidence = false;
              for (const aura of itemFull.aurasGranted) {
                switch (aura) {
                  case 'well_fed':
                    result.foodFactor += 5;
                    break;

                  case 'upset_tummy':
                    if (result.hasConfidence) {
                      subtractConfidence = true;
                    }
                    else {
                      result.foodFactor -= 5;
                    }

                    break;

                  case 'lucky':
                    ++result.luckFactor;
                    break;

                  case 'unlucky':
                    if (result.hasConfidence) {
                      subtractConfidence = true;
                    }
                    else {
                      --result.luckFactor;
                    }

                    break;

                  case 'energized':
                    result.energyFactor += 5;
                    break;

                  case 'sluggish':
                    if (result.hasConfidence) {
                      subtractConfidence = true;
                    }
                    else {
                      result.energyFactor -= 5;
                    }

                    break;

                  case 'distracted':
                    if (result.hasConfidence) {
                      subtractConfidence = true;
                    }
                    else {
                      ++result.distractionCounter;
                    }

                    break;

                  case 'stylish':
                    result.styleFactor += 2;
                    break;

                  case 'startled':
                    if (result.hasConfidence) {
                      subtractConfidence = true;
                    }
                    else {
                      ++result.startledCounter;
                    }

                    break;

                  case 'confident':
                    addConfidence = true;
                    break;
                }
              }

              if (subtractConfidence) {
                result.hasConfidence = false;
              }

              if (addConfidence) {
                result.hasConfidence = true;
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
