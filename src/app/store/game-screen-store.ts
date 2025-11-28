import { withImmutableState, withStorageSync } from '@angular-architects/ngrx-toolkit';
import { computed, type Signal, signal } from '@angular/core';
import { patchState, signalStore, withMethods, withProps } from '@ngrx/signals';
import type { ElementSize } from '../utils/element-size';

export type GameTab = 'map' | 'text-client' | 'arcade';

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

const dummyScreenSizeSignal = signal<ElementSize>({ clientHeight: 0, clientWidth: 0, scrollWidth: 0, scrollHeight: 0 }).asReadonly();
export const GameScreenStore = signalStore(
  withImmutableState(initialState),
  withStorageSync(STORAGE_KEY),
  withProps(() => {
    const screenSizeSignalSignal = signal<Signal<ElementSize> | null>(null);
    const initialScreenSizeSignal = computed(() => (screenSizeSignalSignal() ?? dummyScreenSizeSignal)());
    return {
      _screenSizeSignalSignal: screenSizeSignalSignal,
      screenSizeSignal: initialScreenSizeSignal,
    };
  }),
  withMethods(store => ({
    setScreenSizeSignal(screenSizeSignal: Signal<ElementSize>) {
      store._screenSizeSignalSignal.update((prev) => {
        if (prev !== null) {
          throw new Error('already set');
        }
        return screenSizeSignal;
      });
      store.screenSizeSignal = screenSizeSignal;
    },
    updateLeftSize(leftSize: number) {
      patchState(store, { leftSize });
    },
    updateCurrentTab(currentTab: GameTab) {
      if (currentTab !== 'arcade') {
        patchState(store, { currentTab });
      }
    },
  })),
);
