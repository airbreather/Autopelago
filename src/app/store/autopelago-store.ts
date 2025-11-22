import { effect } from '@angular/core';

import { patchState, signalStore, withHooks, withMethods, withState } from '@ngrx/signals';
import type { SayPacket } from 'archipelago.js';
import { List, Set as ImmutableSet } from 'immutable';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
} from '../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../data/slot-data';
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
  withMethods(store => ({
    init(game: AutopelagoClientAndData) {
      const { connectScreenState, client, pkg, slotData, storedData, locationIsProgression, locationIsTrap } = game;

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

      store.receiveItems(itemsJustReceived);
      client.items.on('itemsReceived', (items) => {
        const itemsJustReceived: number[] = [];
        for (const item of items) {
          const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
          if (typeof itemKey === 'number') {
            itemsJustReceived.push(itemKey);
          }
        }

        store.receiveItems(itemsJustReceived);
      });

      client.room.on('locationsChecked', (locations) => {
        patchState(store, ({ checkedLocations }) => ({
          checkedLocations: checkedLocations.union(locations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1)),
        }));
      });

      store.registerCallback(store.advance);
      store._initTimer({
        minDurationMilliseconds: connectScreenState.minTimeSeconds * 1000,
        maxDurationMilliseconds: connectScreenState.maxTimeSeconds * 1000,
      });
    },
  })),
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
