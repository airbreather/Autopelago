import { effect } from '@angular/core';
import { signalStore, withState, withMethods, patchState, withHooks } from '@ngrx/signals';

const ALL_GAME_TABS_ARRAY = ['map', 'arcade'] as const;
const ALL_GAME_TABS = new Set<string>(ALL_GAME_TABS_ARRAY);
export type GameTab = typeof ALL_GAME_TABS_ARRAY[number];

// Define the state interface
export interface GameScreenState {
  leftSize: number;
  currentTab: GameTab;
  paused: boolean;
}

// Default state
const initialState: GameScreenState = {
  leftSize: 20,
  currentTab: 'map',
  paused: false,
};

// Local storage key
const STORAGE_KEY = 'autopelago-game-state';

// Helper functions for local storage
function loadFromStorage(): Partial<GameScreenState> {
  let result: unknown;
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    result = stored && JSON.parse(stored);
  } catch {
    // Silently fail if localStorage is not available
    result = null;
  }

  if (!(result && typeof result === 'object')) {
    return {};
  }

  if ('leftSize' in result) {
    if (!(typeof result.leftSize === 'number' && result.leftSize >= 5 && result.leftSize <= 95)) {
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
        } catch {
          // Silently fail if localStorage is not available
        }
      });
    }
  }),
  withMethods((store) => ({
    updateLeftSize(leftSize: number) {
      patchState(store, { leftSize });
    },
    restoreDefaultLeftSize() {
      patchState(store, { leftSize: initialState.leftSize });
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
      patchState(store, s => ({ ...s, paused: !s.paused }));
    },
  })),
);
