import { DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Application, Container, Ticker } from 'pixi.js';

import { patchState, signalStore, withHooks, withMethods, withProps, withState } from '@ngrx/signals';

import { MessageNode } from 'archipelago.js';

import { ArchipelagoClient } from '../archipelago-client';
import { resizeEvents } from '../util';

export interface Message {
  readonly ts: Date;
  readonly originalNodes: readonly MessageNode[];
}

export interface RatPixiPlugin {
  destroyRef?: DestroyRef;
  beforeInit?(this: void, app: Application, root: Container): PromiseLike<void> | void;
  afterInit?(this: void, app: Application, root: Container): PromiseLike<void> | void;
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
    pixiApplication: null as Application | null,
    _plugins: signal<readonly RatPixiPlugin[]>([]),
  })),
  withMethods((store, destroyRef = inject(DestroyRef)) => ({
    registerPlugin(plugin: RatPixiPlugin) {
      store._plugins.update(p => [...p, plugin]);
      if (plugin.destroyRef) {
        plugin.destroyRef.onDestroy(() => {
          store._plugins.update(p => p.filter(x => x !== plugin));
        });
      }
    },
    async initInterface(canvas: HTMLCanvasElement, outer: HTMLDivElement) {
      if (store.pixiApplication) {
        throw new Error('Already initialized');
      }

      store.pixiApplication = new Application();
      const reciprocalOriginalWidth = 1 / 300.0;
      const reciprocalOriginalHeight = 1 / 450.0;
      for (const plugin of store._plugins()) {
        if (plugin.beforeInit) {
          await plugin.beforeInit(store.pixiApplication, store.pixiApplication.stage);
          if (store.paused()) {
            Ticker.shared.stop();
          }
        }
      }

      await store.pixiApplication.init({ canvas, resizeTo: outer, backgroundAlpha: 0, antialias: false, sharedTicker: true, autoStart: false });
      resizeEvents(canvas).pipe(
        // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
        takeUntilDestroyed(destroyRef),
      ).subscribe(({ target }) => {
        if (store.pixiApplication) {
          store.pixiApplication.stage.scale.x = target.width * reciprocalOriginalWidth;
          store.pixiApplication.stage.scale.y = target.height * reciprocalOriginalHeight;
          store.pixiApplication.resize();
        }
      });
      for (const plugin of store._plugins()) {
        if (plugin.afterInit) {
          await plugin.afterInit(store.pixiApplication, store.pixiApplication.stage);
          if (store.paused()) {
            Ticker.shared.stop();
          }
        }
      }

      if (store.paused()) {
        store.pixiApplication.resize();
        store.pixiApplication.render();
      }
      else {
        Ticker.shared.start();
      }
    },
    destroyInterface() {
      if (store.pixiApplication) {
        store.pixiApplication.destroy();
        store.pixiApplication = null;
      }
      else {
        throw new Error('Not initialized');
      }
    },
    appendMessage(message: Message) {
      patchState(store, s => ({ messages: [...s.messages, message] }));
    },
    pause() {
      if (!store.paused()) {
        patchState(store, { paused: true });
        Ticker.shared.stop();
      }
    },
    unpause() {
      if (store.paused()) {
        patchState(store, { paused: false });
        Ticker.shared.start();
      }
    },
  })),
  withMethods(store => ({
    togglePause() {
      if (store.paused()) {
        store.unpause();
      }
      else {
        store.pause();
      }
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
  }),
);
