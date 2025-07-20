import { effect } from '@angular/core';

import { patchState, signalStore, withHooks, withMethods, withProps, withState } from '@ngrx/signals';
import { MessageNode } from 'archipelago.js';

const ALL_GAME_TABS_ARRAY = ['map', 'text-client', 'arcade'] as const;
const ALL_GAME_TABS = new Set<string>(ALL_GAME_TABS_ARRAY);
export type GameTab = typeof ALL_GAME_TABS_ARRAY[number];

export interface Message {
  readonly ts: Date;
  readonly originalNodes: readonly MessageNode[];
}

// Define the state interface
export interface GameScreenState {
  leftSize: number | null;
  currentTab: GameTab;
  paused: boolean;
  messages: readonly Message[];
}

// Default state
const initialState: GameScreenState = {
  leftSize: null,
  currentTab: 'map',
  paused: false,
  messages: [],
};

// Local storage key
const STORAGE_KEY = 'autopelago-game-state';

// Helper functions for local storage
function loadFromStorage(): Partial<GameScreenState> {
  let result: unknown;
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    result = stored && JSON.parse(stored);
  }
  catch {
    // Silently fail if localStorage is not available
    result = null;
  }

  if (!(result && typeof result === 'object')) {
    return {};
  }

  if ('leftSize' in result) {
    if (typeof result.leftSize !== 'number') {
      delete result.leftSize;
    }
  }

  if ('currentTab' in result) {
    if (!(typeof result.currentTab === 'string' && ALL_GAME_TABS.has(result.currentTab))) {
      delete result.currentTab;
    }
  }

  if ('paused' in result) {
    if (!(typeof result.paused === 'boolean')) {
      delete result.paused;
    }
  }

  if ('messages' in result) {
    if (!Array.isArray(result.messages)) {
      delete result.messages;
    }
  }

  return result;
}

export const GameScreenStore = signalStore(
  { providedIn: 'root' },
  withState(() => ({
    ...initialState,
    ...loadFromStorage(),
  })),
  withHooks({
    onInit(store) {
      effect(() => {
        try {
          localStorage.setItem(STORAGE_KEY, JSON.stringify({
            leftSize: store.leftSize(),
            currentTab: store.currentTab(),
            paused: store.paused(),
          }));
        }
        catch {
          // Silently fail if localStorage is not available
        }
      });
    },
  }),
  withMethods(store => ({
    updateLeftSize(leftSize: number) {
      patchState(store, { leftSize });
    },
    updateCurrentTab(currentTab: GameTab) {
      if (currentTab !== 'arcade') {
        patchState(store, { currentTab });
      }
    },
    appendMessage(message: Message) {
      patchState(store, s => ({ messages: [...s.messages, message] }));
    },
    pause() {
      if (!store.paused()) {
        patchState(store, { paused: true });
      }
    },
    unpause() {
      if (store.paused()) {
        patchState(store, { paused: false });
      }
    },
    togglePause() {
      patchState(store, s => ({ paused: !s.paused }));
    },
  })),
);
