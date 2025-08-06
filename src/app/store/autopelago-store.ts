import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Application, Container, Ticker } from 'pixi.js';

import { MessageNode } from 'archipelago.js';

import { ArchipelagoClient } from '../archipelago-client';
import { resizeEvents } from '../util';

export interface Message {
  ts: Date;
  originalNodes: readonly Readonly<MessageNode>[];
}

export interface RatPixiPlugin {
  destroyRef?: DestroyRef;
  afterInit?(this: void, app: Application, root: Container): PromiseLike<void> | void;
}

// Define the state interface
export interface GameState {
  paused: boolean;
  messages: readonly Message[];
  foodFactor: number;
  luckFactor: number;
  energyFactor: number;
  styleFactor: number;
  distractionCounter: number;
  startledCounter: number;
  hasConfidence: boolean;
  mercyFactor: number;
  sluggishCarryover: boolean;
}

// Local storage key
const STORAGE_KEY = 'autopelago-game-state';

// Helper functions for local storage
function loadFromStorage(): Partial<Pick<GameState, 'paused'>> {
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

  return result;
}

@Injectable({ providedIn: 'root' })
export class GameStoreService {
  readonly #paused = signal(false);
  readonly #messages = signal<readonly Message[]>([]);
  readonly #foodFactor = signal(0);
  readonly #luckFactor = signal(0);
  readonly #energyFactor = signal(0);
  readonly #styleFactor = signal(0);
  readonly #distractionCounter = signal(0);
  readonly #startledCounter = signal(0);
  readonly #hasConfidence = signal(false);
  readonly #mercyFactor = signal(0);
  readonly #sluggishCarryover = signal(false);

  readonly paused = this.#paused.asReadonly();
  readonly messages = this.#messages.asReadonly();
  readonly foodFactor = this.#foodFactor.asReadonly();
  readonly luckFactor = this.#luckFactor.asReadonly();
  readonly energyFactor = this.#energyFactor.asReadonly();
  readonly styleFactor = this.#styleFactor.asReadonly();
  readonly distractionCounter = this.#distractionCounter.asReadonly();
  readonly startledCounter = this.#startledCounter.asReadonly();
  readonly hasConfidence = this.#hasConfidence.asReadonly();
  readonly mercyFactor = this.#mercyFactor.asReadonly();
  readonly sluggishCarryover = this.#sluggishCarryover.asReadonly();

  #pixiApplication: Application | null = null;
  readonly #plugins = signal<readonly RatPixiPlugin[]>([]);

  readonly #destroyRef = inject(DestroyRef);

  constructor() {
    // Local storage key
    const STORAGE_KEY = 'autopelago-connect-screen-state';
    try {
      const storedJSON = localStorage.getItem(STORAGE_KEY);
      if (storedJSON) {
        const stored = JSON.parse(storedJSON) as unknown;
        if (stored && typeof stored === 'object') {
          if ('paused' in stored && typeof stored.paused === 'boolean') {
            this.#paused.set(stored.paused);
          }
        }
      }
    }
    catch {
      // Silently fail if localStorage is not available
    }
    const ap = inject(ArchipelagoClient);
    ap.events('messages', 'message')
      .pipe(takeUntilDestroyed())
      .subscribe((msg) => {
        this.appendMessage({ ts: new Date(), originalNodes: msg[1] });
      });

    const gs = loadFromStorage();
    this.#paused.set(!!gs.paused);
  }

  registerPlugin(plugin: Readonly<RatPixiPlugin>) {
    this.#plugins.update(p => [...p, plugin]);
    if (plugin.destroyRef) {
      plugin.destroyRef.onDestroy(() => {
        this.#plugins.update(p => p.filter(x => x !== plugin));
      });
    }
  }

  async initInterface(canvas: HTMLCanvasElement, outer: HTMLDivElement, interfaceDestroyRef: DestroyRef) {
    if (this.#pixiApplication) {
      throw new Error('Already initialized');
    }

    Ticker.shared.stop();
    const app = this.#pixiApplication = new Application();
    interfaceDestroyRef.onDestroy(() => {
      app.destroy();
      this.#pixiApplication = null;
    });
    const reciprocalOriginalWidth = 1 / canvas.width;
    const reciprocalOriginalHeight = 1 / canvas.height;
    await app.init({ canvas, resizeTo: outer, backgroundAlpha: 0, antialias: false, sharedTicker: true, autoStart: false });
    Ticker.shared.stop();

    resizeEvents(outer).pipe(
      // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
      takeUntilDestroyed(interfaceDestroyRef),
      takeUntilDestroyed(this.#destroyRef),
    ).subscribe(({ target }) => {
      app.stage.scale.x = target.clientWidth * reciprocalOriginalWidth;
      app.stage.scale.y = target.clientHeight * reciprocalOriginalHeight;
      app.resize();
    });

    for (const plugin of this.#plugins()) {
      if (plugin.afterInit) {
        await plugin.afterInit(app, app.stage);
        Ticker.shared.stop();
      }
    }

    if (this.#paused()) {
      app.resize();
      app.render();
    }
    else {
      Ticker.shared.start();
    }
  }

  appendMessage(message: Readonly<Message>) {
    this.#messages.update(m => [...m, message]);
  }

  pause() {
    this.#paused.set(true);
    Ticker.shared.stop();
  }

  unpause() {
    this.#paused.set(false);
    Ticker.shared.start();
  }

  togglePause() {
    if (this.#paused()) {
      this.unpause();
    }
    else {
      this.pause();
    }
  }
}
