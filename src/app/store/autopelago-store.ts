import { withImmutableState } from '@angular-architects/ngrx-toolkit';
import { computed } from '@angular/core';
import BitArray from '@bitarray/typedarray';

import { patchState, signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import { List, Set as ImmutableSet } from 'immutable';
import Queue from 'yocto-queue';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
  type VictoryLocationYamlKey,
} from '../data/resolved-definitions';
import type { AutopelagoClientAndData, AutopelagoStoredData } from '../data/slot-data';
import type { DerivedGameState } from '../game/derived-state';
import {
  type LocationEvidence,
  locationEvidenceFromJSONSerializable,
  locationEvidenceToJSONSerializable,
} from '../game/location-evidence';
import derive from '../game/state-functions/derive';
import { withCleverTimer } from './with-clever-timer';
import { withGameState } from './with-game-state';

const initialState = {
  lactoseIntolerant: false,
  victoryLocationYamlKey: null as VictoryLocationYamlKey | null,
  workDone: NaN,
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
  previousLocationEvidence: null as LocationEvidence,
  priorityPriorityLocations: List<number>(),
  priorityLocations: List<number>(),
  receivedItems: List<number>(),
  receivedItemCountLookup: List<number>(Array<number>(BAKED_DEFINITIONS_FULL.allItems.length).fill(0)),
  checkedLocations: ImmutableSet<number>(),
};

export const GameStore = signalStore(
  withImmutableState(initialState),
  withCleverTimer(),
  withGameState(),
  withProps(() => ({
    locationIsProgression: new BitArray(0),
    locationIsTrap: new BitArray(0),
  })),
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
      previousLocationEvidence: locationEvidenceToJSONSerializable(store.previousLocationEvidence()),
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
      const gameState = store.gameState();
      if (!gameState) {
        return;
      }

      let derived: DerivedGameState | null = null;
      const derivedGameState = () => derived ??= derive(gameState);
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
          priorityPriorityLocations: prev.priorityPriorityLocations,
        } satisfies Partial<typeof initialState>;
        result.priorityPriorityLocations = result.priorityPriorityLocations.withMutations((p) => {
          result.receivedItemCountLookup = result.receivedItemCountLookup.withMutations((l) => {
            result.receivedItems = prev.receivedItems.withMutations((r) => {
              const locs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[gameState.victoryLocationYamlKey].allLocations;
              const validProgressionItems = new BitArray(store.locationIsProgression);
              const validTrapItems = new BitArray(store.locationIsTrap);
              for (const loc of [...prev.checkedLocations, ...prev.priorityPriorityLocations]) {
                validProgressionItems[loc] = 0;
                validTrapItems[loc] = 0;
              }
              function addLocation(include: BitArray) {
                const { regionIsHardLocked } = derivedGameState();
                const visited = new BitArray(include.length);
                const q = new Queue<number>();

                function tryEnqueue(loc: number) {
                  if (visited[loc]) {
                    return;
                  }

                  if (!regionIsHardLocked[locs[loc].regionLocationKey[0]]) {
                    q.enqueue(loc);
                  }

                  visited[loc] = 1;
                }

                tryEnqueue(prev.currentLocation);
                for (let loc = q.dequeue(); loc !== undefined; loc = q.dequeue()) {
                  if (include[loc]) {
                    include[loc] = 0;
                    p.push(loc);
                    break;
                  }

                  for (const [c] of locs[loc].connected.all) {
                    tryEnqueue(c);
                  }
                }
              }

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

                    case 'smart':
                      addLocation(validProgressionItems);
                      break;

                    case 'conspiratorial':
                      if (result.hasConfidence) {
                        subtractConfidence = true;
                      }
                      else {
                        addLocation(validTrapItems);
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
        });

        result.processedReceivedItemCount = result.receivedItems.size;
        return result;
      });
    },
  })),
  withMethods(store => ({
    init(game: AutopelagoClientAndData) {
      const { connectScreenStore, client, pkg, slotData, storedData, locationIsProgression, locationIsTrap } = game;

      // BitArray doesn't fit in a SignalStore's state.
      store.locationIsProgression = locationIsProgression;
      store.locationIsTrap = locationIsTrap;

      const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];

      const locationNameLookup = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].locationNameLookup;
      patchState(store, {
        ...storedData,
        lactoseIntolerant: slotData.lactose_intolerant,
        victoryLocationYamlKey,
        priorityLocations: List(storedData.priorityLocations),
        priorityPriorityLocations: List(storedData.priorityPriorityLocations),
        receivedItems: List<number>(),
        receivedItemCountLookup: List<number>(Array<number>(BAKED_DEFINITIONS_FULL.allItems.length).fill(0)),
        checkedLocations: ImmutableSet(client.room.checkedLocations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1)),
        previousLocationEvidence: locationEvidenceFromJSONSerializable(storedData.previousLocationEvidence),
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

      client.room.on('locationsChecked', (locations) => {
        patchState(store, ({ checkedLocations }) => ({
          checkedLocations: checkedLocations.union(locations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1)),
        }));
      });

      store.registerCallback(() => {
        store.advance();
      });
      store._initTimer({
        minDuration: connectScreenStore.minTime(),
        maxDuration: connectScreenStore.maxTime(),
      });
    },
  })),
);
