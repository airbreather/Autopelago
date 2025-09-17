import { patchState, signalStore, withMethods } from '@ngrx/signals';
import { withImmutableState, withStorageSync } from '@angular-architects/ngrx-toolkit';

import { type MessageNode } from 'archipelago.js';

export interface Message {
  ts: Date;
  originalNodes: readonly Readonly<MessageNode>[];
}

const initialState = {
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
};

// Local storage key
const STORAGE_KEY = 'autopelago-game-state';

export const GameStore = signalStore(
  { providedIn: 'root' },
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
  })),
);
