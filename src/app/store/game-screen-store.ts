import { signalStore, withState, withMethods, patchState, withHooks } from '@ngrx/signals';
import { effect } from '@angular/core';

// Define the state interface
export interface GameScreenState {
  leftSize: number;
}

// Default state
const initialState: GameScreenState = {
  leftSize: 20,
};

// Local storage key
const STORAGE_KEY = 'autopelago-connect-game-state';

// Helper functions for local storage
function loadFromStorage(): Partial<GameScreenState> {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored ? JSON.parse(stored) as Partial<GameScreenState> : {};
  } catch {
    return {};
  }
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
  })),
);
