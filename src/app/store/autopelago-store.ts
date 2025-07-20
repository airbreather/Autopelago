import { effect, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Subscription } from 'rxjs';

import { patchState, signalStore, withHooks, withMethods, withProps, withState } from '@ngrx/signals';

import { MessageNode } from 'archipelago.js';

import { ArchipelagoClient } from '../archipelago-client';

export interface Message {
  readonly ts: Date;
  readonly originalNodes: readonly MessageNode[];
}

// Define the state interface
export interface GameState {
  paused: boolean;
  messages: readonly Message[];
}

// Default state
const initialState: GameState = {
  paused: false,
  messages: [],
};

// Local storage key
const STORAGE_KEY = 'autopelago-game-state';

// Helper functions for local storage
function loadFromStorage(): Partial<GameState> {
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

export const GameStore = signalStore(
  { providedIn: 'root' },
  withState(() => ({
    ...initialState,
    ...loadFromStorage(),
  })),
  withProps(() => ({
    _subscription: new Subscription(),
  })),
  withMethods(store => ({
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
  withHooks({
    onInit(store, ap = inject(ArchipelagoClient)) {
      ap.events('messages', 'message')
        .pipe(takeUntilDestroyed())
        .subscribe((msg) => {
          store.appendMessage({ ts: new Date(), originalNodes: msg[1] });
        });
    },
    onDestroy(store) {
      store._subscription.unsubscribe();
    },
  }),
);
