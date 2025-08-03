import { effect, Injectable, signal } from '@angular/core';

const ALL_GAME_TABS_ARRAY = ['map', 'text-client', 'arcade'] as const;
const ALL_GAME_TABS = new Set<string>(ALL_GAME_TABS_ARRAY);
export type GameTab = typeof ALL_GAME_TABS_ARRAY[number];

// Define the state interface
export interface GameScreenState {
  leftSize: number | null;
  currentTab: GameTab;
}

// Default state
const initialState: GameScreenState = {
  leftSize: null,
  currentTab: 'map',
};

// Local storage key
const STORAGE_KEY = 'autopelago-game-screen-state';

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

  return result;
}

@Injectable({ providedIn: 'root' })
export class GameScreenStoreService {
  // State signals
  readonly #leftSize = signal<number | null>(initialState.leftSize);
  readonly #currentTab = signal<GameTab>(initialState.currentTab);

  // Public readonly signals
  readonly leftSize = this.#leftSize.asReadonly();
  readonly currentTab = this.#currentTab.asReadonly();

  constructor() {
    // Load from storage and update signals
    const stored = loadFromStorage();
    if (stored.leftSize !== undefined) {
      this.#leftSize.set(stored.leftSize);
    }
    if (stored.currentTab !== undefined) {
      this.#currentTab.set(stored.currentTab);
    }

    // Auto-save to localStorage
    effect(() => {
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify({
          leftSize: this.leftSize(),
          currentTab: this.currentTab(),
        }));
      }
      catch {
        // Silently fail if localStorage is not available
      }
    });
  }

  // Methods
  updateLeftSize(leftSize: number) {
    this.#leftSize.set(leftSize);
  }

  updateCurrentTab(currentTab: GameTab) {
    if (currentTab !== 'arcade') {
      this.#currentTab.set(currentTab);
    }
  }
}
