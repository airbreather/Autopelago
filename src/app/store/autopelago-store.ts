import { inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { MessageNode } from 'archipelago.js';

import { ArchipelagoClient } from '../archipelago-client';

export interface Message {
  ts: Date;
  originalNodes: readonly Readonly<MessageNode>[];
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

  appendMessage(message: Readonly<Message>) {
    this.#messages.update(m => [...m, message]);
  }

  pause() {
    this.#paused.set(true);
  }

  unpause() {
    this.#paused.set(false);
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
