import BitArray from '@bitarray/typedarray';

import { patchState, signalStore, withMethods } from '@ngrx/signals';
import { List, Set as ImmutableSet } from 'immutable';
import Queue from 'yocto-queue';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
} from '../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../data/slot-data';
import type { DefiningGameState } from '../game/defining-state';
import { isDone, performTurnAction, startTurn } from '../game/state-functions';
import derive from '../game/state-functions/derive';
import { targetLocationEvidenceFromJSONSerializable } from '../game/target-location-evidence';
import type { TurnState } from '../game/turn-state';
import { withCleverTimer } from './with-clever-timer';
import { withGameState } from './with-game-state';

export const GameStore = signalStore(
  withCleverTimer(),
  withGameState(),
  withMethods((store) => {
    function _receiveItems(items: Iterable<number>) {
      const { allItems, allLocations } = store.defs();
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
          processedReceivedItemCount: prev.processedReceivedItemCount,
          auraDrivenLocations: prev.auraDrivenLocations,
          userRequestedLocations: prev.userRequestedLocations,
        } satisfies Partial<DefiningGameState>;
        result.auraDrivenLocations = result.auraDrivenLocations.withMutations((a) => {
          result.receivedItems = prev.receivedItems.withMutations((r) => {
            const locs = allLocations;
            const validProgressionItems = new BitArray(prev.locationIsProgression);
            const validTrapItems = new BitArray(prev.locationIsTrap);
            for (const loc of [...prev.checkedLocations, ...prev.auraDrivenLocations]) {
              validProgressionItems[loc] = 0;
              validTrapItems[loc] = 0;
            }
            function addLocation(include: BitArray) {
              const { regionIsHardLocked } = store._regionLocks();
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
                  a.push(loc);
                  break;
                }

                for (const [c] of locs[loc].connected.all) {
                  tryEnqueue(c);
                }
              }
            }

            for (const item of items) {
              const itemFull = allItems[item];
              r.push(item);
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

        result.processedReceivedItemCount = result.receivedItems.size;
        return result;
      });
    }

    function init(game: AutopelagoClientAndData) {
      const { connectScreenStore, client, pkg, slotData, storedData, locationIsProgression, locationIsTrap } = game;

      const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];

      const locationNameLookup = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].locationNameLookup;
      patchState(store, {
        ...storedData,
        locationIsProgression,
        locationIsTrap,
        lactoseIntolerant: slotData.lactose_intolerant,
        victoryLocationYamlKey,
        auraDrivenLocations: List(storedData.auraDrivenLocations),
        userRequestedLocations: List(storedData.userRequestedLocations),
        receivedItems: List<number>(),
        checkedLocations: ImmutableSet(client.room.checkedLocations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1)),
        previousTargetLocationEvidence: targetLocationEvidenceFromJSONSerializable(storedData.previousTargetLocationEvidence),
      });
      const itemsJustReceived: number[] = [];
      for (const item of client.items.received) {
        const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
        if (typeof itemKey === 'number') {
          itemsJustReceived.push(itemKey);
        }
      }

      _receiveItems(itemsJustReceived);
      client.items.on('itemsReceived', (items) => {
        const itemsJustReceived: number[] = [];
        for (const item of items) {
          const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
          if (typeof itemKey === 'number') {
            itemsJustReceived.push(itemKey);
          }
        }

        _receiveItems(itemsJustReceived);
      });

      client.room.on('locationsChecked', (locations) => {
        patchState(store, ({ checkedLocations }) => ({
          checkedLocations: checkedLocations.union(locations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1)),
        }));
      });

      store.registerCallback(advance);
      store._initTimer({
        minDuration: connectScreenStore.minTime() * 1000,
        maxDuration: connectScreenStore.maxTime() * 1000,
      });
    }
    function advance() {
      const gameState: DefiningGameState = {
        lactoseIntolerant: store.lactoseIntolerant(),
        victoryLocationYamlKey: store.victoryLocationYamlKey(),
        enabledBuffs: store.enabledBuffs(),
        enabledTraps: store.enabledTraps(),
        locationIsProgression: store.locationIsProgression(),
        locationIsTrap: store.locationIsTrap(),
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
        workDone: store.workDone(),
        auraDrivenLocations: store.auraDrivenLocations(),
        userRequestedLocations: store.userRequestedLocations(),
        previousTargetLocationEvidence: store.previousTargetLocationEvidence(),
        receivedItems: store.receivedItems(),
        checkedLocations: store.checkedLocations(),
        prng: store.prng(),
      };

      if (isDone(gameState)) {
        return;
      }

      let turnState: TurnState = startTurn(derive(gameState));
      while (turnState.remainingActions > 0) {
        turnState = performTurnAction(turnState);
      }
    }

    return {
      _receiveItems,
      init,
      advance,
    };
  }),
);
