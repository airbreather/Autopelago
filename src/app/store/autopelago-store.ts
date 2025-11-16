import { effect } from '@angular/core';
import BitArray from '@bitarray/typedarray';

import { patchState, signalStore, withHooks, withMethods, withState } from '@ngrx/signals';
import type { SayPacket } from 'archipelago.js';
import { List, Set as ImmutableSet } from 'immutable';
import Queue from 'yocto-queue';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
} from '../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../data/slot-data';
import type { DefiningGameState } from '../game/defining-state';
import { targetLocationEvidenceFromJSONSerializable } from '../game/target-location-evidence';
import { withCleverTimer } from './with-clever-timer';
import { withGameState } from './with-game-state';

export const GameStore = signalStore(
  withCleverTimer(),
  withGameState(),
  withState({
    game: null as AutopelagoClientAndData | null,
    processedMessageCount: 0,
  }),
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
              if (r.size <= result.processedReceivedItemCount) {
                continue;
              }

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

            // Startled is extremely punishing. after a big release, it can be very annoying to just
            // sit there and wait for too many turns in a row. same concept applies to Distracted.
            result.startledCounter = Math.min(result.startledCounter, 3);
            result.distractionCounter = Math.min(result.distractionCounter, 3);
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
        game,
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

      store.registerCallback(store.advance);
      store._initTimer({
        minDurationMilliseconds: connectScreenStore.minTimeSeconds() * 1000,
        maxDurationMilliseconds: connectScreenStore.maxTimeSeconds() * 1000,
      });
    }

    return {
      _receiveItems,
      init,
    };
  }),
  withHooks({
    onInit(store) {
      effect(() => {
        const game = store.game();
        if (!game) {
          return;
        }

        const outgoingCheckedLocations = store.outgoingCheckedLocations();
        if (outgoingCheckedLocations.size > 0) {
          const defs = store.defs();
          const locationNameLookup = game.pkg.locationTable;
          game.client.check(...outgoingCheckedLocations.map(l => locationNameLookup[defs.allLocations[l].name]));
          patchState(store, { outgoingCheckedLocations: outgoingCheckedLocations.clear() });
        }
      });

      const goalEffect = effect(() => {
        const game = store.game();
        if (!game) {
          return;
        }

        if (store.checkedLocations().has(store.victoryLocation())) {
          game.client.goal();
          goalEffect.destroy();
        }
      });

      effect(() => {
        const game = store.game();
        if (!game) {
          return;
        }

        const client = game.client;
        const players = client.players;
        let processedMessageCount = store.processedMessageCount();
        for (const message of game.messageLog().skip(processedMessageCount)) {
          store.processMessage(message, players);
          ++processedMessageCount;
        }

        patchState(store, ({ outgoingMessages }) => {
          if (outgoingMessages.size > 0) {
            client.socket.send(...outgoingMessages.map(msg => ({
              cmd: 'Say',
              text: msg,
            } satisfies SayPacket)));
          }

          return {
            outgoingMessages: outgoingMessages.clear(),
            processedMessageCount,
          };
        });
      });
    },
  }),
);
