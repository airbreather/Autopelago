import { withImmutableState, withStorageSync } from '@angular-architects/ngrx-toolkit';
import { patchState, signalStore, withMethods } from '@ngrx/signals';

export type GameTab = 'map' | 'chat' | 'app-build-info' | 'arcade';

// Define the state interface
export interface GameScreenState {
  leftSize: number | null;
  currentTab: GameTab;
  showingPath: boolean;
}

// Default state
const initialState: GameScreenState = {
  leftSize: null,
  currentTab: 'map',
  showingPath: false,
};

// Local storage key
const STORAGE_KEY = 'autopelago-game-screen-state';

export const GameScreenStore = signalStore(
  withImmutableState(initialState),
  withStorageSync(STORAGE_KEY),
  withMethods(store => ({
    updateLeftSize(leftSize: number) {
      patchState(store, { leftSize });
    },
    updateCurrentTab(currentTab: GameTab) {
      if (currentTab !== 'arcade') {
        patchState(store, { currentTab });
      }
    },
    toggleShowingPath() {
      patchState(store, ({ showingPath }) => ({ showingPath: !showingPath }));
    },
  })),
);
