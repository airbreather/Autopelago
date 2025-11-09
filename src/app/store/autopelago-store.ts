import { withImmutableState, withStorageSync } from '@angular-architects/ngrx-toolkit';
import { computed } from '@angular/core';

import { patchState, signalStore, withComputed, withMethods } from '@ngrx/signals';
import { List, Set } from 'immutable';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
  type VictoryLocationYamlKey,
} from '../data/resolved-definitions';
import type { AutopelagoClientAndData, AutopelagoStoredData } from '../data/slot-data';
import {
  type PreviousLocationEvidence,
  previousLocationEvidenceFromJSONSerializable,
  previousLocationEvidenceToJSONSerializable,
} from '../game/previous-location-evidence';
import { withCleverTimer } from './with-clever-timer';

const initialState = {
  lactoseIntolerant: false,
  victoryLocationYamlKey: null as VictoryLocationYamlKey | null,
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
  withCleverTimer(),
  withStorageSync({
    key: STORAGE_KEY,
    select: ({ running }) => ({ paused: !running }),
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
  })),
  withComputed(store => ({
    asStoredData: computed<AutopelagoStoredData>(() => ({
      workDone: store.workDoneSnapshot(),
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
    moveTo(currentLocation: number) {
      patchState(store, { currentLocation });
    },
    checkLocations(locations: Iterable<number>) {
      const locationsArray = [...locations];
      console.log('marking locations checked:', locationsArray);
      patchState(store, ({ checkedLocations }) => ({ checkedLocations: checkedLocations.union(locationsArray) }));
    },
    _receiveItems(items: Iterable<number>) {
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
  withMethods(store => ({
    init(game: AutopelagoClientAndData) {
      const { connectScreenStore, client, slotData, storedData } = game;
      const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];
      const pkg = client.package.findPackage('Autopelago');
      if (!pkg) {
        throw new Error('could not find Autopelago package');
      }

      const locationNameLookup = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].locationNameLookup;
      patchState(store, {
        ...storedData,
        lactoseIntolerant: slotData.lactose_intolerant,
        victoryLocationYamlKey,
        priorityLocations: List(storedData.priorityLocations),
        priorityPriorityLocations: List(storedData.priorityPriorityLocations),
        receivedItems: List<number>(),
        receivedItemCountLookup: List<number>(Array<number>(BAKED_DEFINITIONS_FULL.allItems.length).fill(0)),
        checkedLocations: Set(client.room.checkedLocations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1)),
        previousLocationEvidence: previousLocationEvidenceFromJSONSerializable(storedData.previousLocationEvidence),
      });
      const itemsJustReceived: number[] = [];
      for (const item of client.items.received) {
        const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
        if (typeof itemKey === 'number') {
          itemsJustReceived.push(itemKey);
        }
      }

      store._receiveItems(itemsJustReceived);
      client.items.on('itemsReceived', (items) => {
        const itemsJustReceived: number[] = [];
        for (const item of items) {
          const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
          if (typeof itemKey === 'number') {
            itemsJustReceived.push(itemKey);
          }
        }

        store._receiveItems(itemsJustReceived);
      });
      store._initTimer({
        minDuration: connectScreenStore.minTime(),
        maxDuration: connectScreenStore.maxTime(),
      });

      client.room.on('locationsChecked', (locations) => {
        patchState(store, ({ checkedLocations }) => ({
          checkedLocations: checkedLocations.union(locations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1)),
        }));
      });
    },
  })),
);
