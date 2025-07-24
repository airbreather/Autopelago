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
    async initInterface(canvas: HTMLCanvasElement, outer: HTMLDivElement, interfaceDestroyRef: DestroyRef) {
      if (store.pixiApplication) {
        throw new Error('Already initialized');
      }

      Ticker.shared.stop();
      const app = store.pixiApplication = new Application();
      interfaceDestroyRef.onDestroy(() => {
        app.destroy();
        store.pixiApplication = null;
      });
      const reciprocalOriginalWidth = 1 / canvas.width;
      const reciprocalOriginalHeight = 1 / canvas.height;
      await app.init({ canvas, resizeTo: outer, backgroundAlpha: 0, antialias: false, sharedTicker: true, autoStart: false });
      Ticker.shared.stop();

      resizeEvents(outer).pipe(
        // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
        takeUntilDestroyed(interfaceDestroyRef),
        takeUntilDestroyed(destroyRef),
      ).subscribe(({ target }) => {
        app.stage.scale.x = target.clientWidth * reciprocalOriginalWidth;
        app.stage.scale.y = target.clientHeight * reciprocalOriginalHeight;
        app.resize();
      });

      for (const plugin of store._plugins()) {
        if (plugin.afterInit) {
          await plugin.afterInit(app, app.stage);
          Ticker.shared.stop();
        }
      }

      if (store.paused()) {
        app.resize();
        app.render();
      }
      else {
        Ticker.shared.start();
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
