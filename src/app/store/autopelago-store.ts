import { computed, inject } from '@angular/core';

import { List, Set } from 'immutable';

import { patchState, signalStore, withComputed, withMethods } from '@ngrx/signals';
import { withImmutableState, withStorageSync } from '@angular-architects/ngrx-toolkit';

import { type MessageNode } from 'archipelago.js';

import type { AutopelagoDefinitions } from '../data/resolved-definitions';
import { GameDefinitionsStore } from './game-definitions-store';

export interface Message {
  ts: Date;
  originalNodes: readonly Readonly<MessageNode>[];
}

const initialState = {
  defs: null as AutopelagoDefinitions | null,
  lactoseIntolerant: false,
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
  receivedItems: List<number>(),
  checkedLocations: Set<number>(),
};

// Local storage key
const STORAGE_KEY = 'autopelago-game-state';

export const GameStore = signalStore(
  withComputed((_store, defsStore = inject(GameDefinitionsStore)) => ({
    defs: computed(() => defsStore.resolvedDefs()),
  })),
  withImmutableState(initialState),
  withStorageSync({
    key: STORAGE_KEY,
    select: ({ paused }) => ({ paused }),
  }),
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
    receiveItems(items: Iterable<number>) {
      const lactoseIntolerant = store.lactoseIntolerant();
      const defs = store.defs();
      if (!defs) {
        return;
      }

      patchState(store, (prev) => {
        const result = {
          foodFactor: prev.foodFactor,
          luckFactor: prev.luckFactor,
          energyFactor: prev.energyFactor,
          styleFactor: prev.styleFactor,
          distractionCounter: prev.distractionCounter,
          startledCounter: prev.startledCounter,
          hasConfidence: prev.hasConfidence,
          receivedItems: prev.receivedItems, // will be clobbered. just helping TypeScript.
        } satisfies Partial<typeof initialState>;
        result.receivedItems = prev.receivedItems.withMutations((r) => {
          for (const item of items) {
            const itemFull = defs.allItems[item];
            console.log('received item', lactoseIntolerant ? itemFull.lactoseIntolerantName : itemFull.lactoseName);
            r.push(item);
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

        return result;
      });
    },
  })),
);
