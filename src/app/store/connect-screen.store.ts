import { computed, effect, Injectable, signal } from '@angular/core';

// Define the state interface
export interface ConnectScreenState {
  slot: string;
  directHost: string;
  port: number;
  password: string;
  minTime: number;
  maxTime: number;
  enableTileAnimations: boolean;
  enableRatAnimations: boolean;
  sendChatMessages: boolean;
  whenTargetChanges: boolean;
  whenBecomingBlocked: boolean;
  whenStillBlocked: boolean;
  whenBecomingUnblocked: boolean;
  forOneTimeEvents: boolean;
}

// Default state
const initialState: ConnectScreenState = {
  slot: '',
  directHost: 'archipelago.gg',
  port: 38281,
  password: '',
  minTime: 20,
  maxTime: 30,
  enableTileAnimations: true,
  enableRatAnimations: true,
  sendChatMessages: true,
  whenTargetChanges: true,
  whenBecomingBlocked: true,
  whenStillBlocked: false,
  whenBecomingUnblocked: true,
  forOneTimeEvents: true,
};

// Local storage key
const STORAGE_KEY = 'autopelago-connect-screen-state';

// Helper functions for local storage
function loadFromStorage(): Partial<ConnectScreenState> {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored ? JSON.parse(stored) as Partial<ConnectScreenState> : {};
  }
  catch {
    return {};
  }
}

@Injectable({ providedIn: 'root' })
export class ConnectScreenStoreService {
  // State signals
  readonly #slot = signal(initialState.slot);
  readonly #directHost = signal(initialState.directHost);
  readonly #port = signal(initialState.port);
  readonly #password = signal(initialState.password);
  readonly #minTime = signal(initialState.minTime);
  readonly #maxTime = signal(initialState.maxTime);
  readonly #enableTileAnimations = signal(initialState.enableTileAnimations);
  readonly #enableRatAnimations = signal(initialState.enableRatAnimations);
  readonly #sendChatMessages = signal(initialState.sendChatMessages);
  readonly #whenTargetChanges = signal(initialState.whenTargetChanges);
  readonly #whenBecomingBlocked = signal(initialState.whenBecomingBlocked);
  readonly #whenStillBlocked = signal(initialState.whenStillBlocked);
  readonly #whenBecomingUnblocked = signal(initialState.whenBecomingUnblocked);
  readonly #forOneTimeEvents = signal(initialState.forOneTimeEvents);

  // Public readonly signals
  readonly slot = this.#slot.asReadonly();
  readonly directHost = this.#directHost.asReadonly();
  readonly port = this.#port.asReadonly();
  readonly password = this.#password.asReadonly();
  readonly minTime = this.#minTime.asReadonly();
  readonly maxTime = this.#maxTime.asReadonly();
  readonly enableTileAnimations = this.#enableTileAnimations.asReadonly();
  readonly enableRatAnimations = this.#enableRatAnimations.asReadonly();
  readonly sendChatMessages = this.#sendChatMessages.asReadonly();
  readonly whenTargetChanges = this.#whenTargetChanges.asReadonly();
  readonly whenBecomingBlocked = this.#whenBecomingBlocked.asReadonly();
  readonly whenStillBlocked = this.#whenStillBlocked.asReadonly();
  readonly whenBecomingUnblocked = this.#whenBecomingUnblocked.asReadonly();
  readonly forOneTimeEvents = this.#forOneTimeEvents.asReadonly();

  // Computed signals
  readonly sendChatMessagesWhenTargetChanges = computed(() => this.sendChatMessages() && this.whenTargetChanges());
  readonly sendChatMessagesWhenBecomingBlocked = computed(() => this.sendChatMessages() && this.whenBecomingBlocked());
  readonly sendChatMessagesWhenStillBlocked = computed(() => this.sendChatMessages() && this.whenBecomingBlocked() && this.whenStillBlocked());
  readonly sendChatMessagesWhenBecomingUnblocked = computed(() => this.sendChatMessages() && this.whenBecomingUnblocked());
  readonly sendChatMessagesForOneTimeEvents = computed(() => this.sendChatMessages() && this.forOneTimeEvents());

  constructor() {
    // Load from storage and update signals
    const stored = loadFromStorage();
    if (stored.slot !== undefined) {
      this.#slot.set(stored.slot);
    }
    if (stored.directHost !== undefined) {
      this.#directHost.set(stored.directHost);
    }
    if (stored.port !== undefined) {
      this.#port.set(stored.port);
    }
    if (stored.password !== undefined) {
      this.#password.set(stored.password);
    }
    if (stored.minTime !== undefined) {
      this.#minTime.set(stored.minTime);
    }
    if (stored.maxTime !== undefined) {
      this.#maxTime.set(stored.maxTime);
    }
    if (stored.enableTileAnimations !== undefined) {
      this.#enableTileAnimations.set(stored.enableTileAnimations);
    }
    if (stored.enableRatAnimations !== undefined) {
      this.#enableRatAnimations.set(stored.enableRatAnimations);
    }
    if (stored.sendChatMessages !== undefined) {
      this.#sendChatMessages.set(stored.sendChatMessages);
    }
    if (stored.whenTargetChanges !== undefined) {
      this.#whenTargetChanges.set(stored.whenTargetChanges);
    }
    if (stored.whenBecomingBlocked !== undefined) {
      this.#whenBecomingBlocked.set(stored.whenBecomingBlocked);
    }
    if (stored.whenStillBlocked !== undefined) {
      this.#whenStillBlocked.set(stored.whenStillBlocked);
    }
    if (stored.whenBecomingUnblocked !== undefined) {
      this.#whenBecomingUnblocked.set(stored.whenBecomingUnblocked);
    }
    if (stored.forOneTimeEvents !== undefined) {
      this.#forOneTimeEvents.set(stored.forOneTimeEvents);
    }

    // Auto-save to localStorage
    effect(() => {
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify({
          slot: this.slot(),
          directHost: this.directHost(),
          port: this.port(),
          password: this.password(),
          minTime: this.minTime(),
          maxTime: this.maxTime(),
          enableTileAnimations: this.enableTileAnimations(),
          enableRatAnimations: this.enableRatAnimations(),
          sendChatMessages: this.sendChatMessages(),
          whenTargetChanges: this.whenTargetChanges(),
          whenBecomingBlocked: this.whenBecomingBlocked(),
          whenStillBlocked: this.whenStillBlocked(),
          whenBecomingUnblocked: this.whenBecomingUnblocked(),
          forOneTimeEvents: this.forOneTimeEvents(),
        }));
      }
      catch {
        // Silently fail if localStorage is not available
      }
    });
  }

  // Methods
  updateSlot(slot: string) {
    this.#slot.set(slot);
  }

  updateDirectHost(directHost: string) {
    const m = /(?<=:)\d+$/.exec(directHost);
    if (m) {
      const port = Number(m[0]);
      this.#directHost.set(directHost);
      this.#port.set(port);
    }
    else {
      this.#directHost.set(directHost);
    }
  }

  updatePassword(password: string) {
    this.#password.set(password);
  }

  updateMinTime(minTime: number) {
    this.#minTime.set(minTime);
  }

  updateMaxTime(maxTime: number) {
    this.#maxTime.set(maxTime);
  }

  updateEnableTileAnimations(enableTileAnimations: boolean) {
    this.#enableTileAnimations.set(enableTileAnimations);
  }

  updateEnableRatAnimations(enableRatAnimations: boolean) {
    this.#enableRatAnimations.set(enableRatAnimations);
  }

  updateSendChatMessages(sendChatMessages: boolean) {
    this.#sendChatMessages.set(sendChatMessages);
  }

  updateWhenTargetChanges(whenTargetChanges: boolean) {
    this.#whenTargetChanges.set(whenTargetChanges);
  }

  updateWhenBecomingBlocked(whenBecomingBlocked: boolean) {
    this.#whenBecomingBlocked.set(whenBecomingBlocked);
  }

  updateWhenStillBlocked(whenStillBlocked: boolean) {
    this.#whenStillBlocked.set(whenStillBlocked);
  }

  updateWhenBecomingUnblocked(whenBecomingUnblocked: boolean) {
    this.#whenBecomingUnblocked.set(whenBecomingUnblocked);
  }

  updateForOneTimeEvents(forOneTimeEvents: boolean) {
    this.#forOneTimeEvents.set(forOneTimeEvents);
  }

  updatePort(port: number) {
    this.#port.set(port);
  }
}

export function createHostSelector(store: ConnectScreenStoreService) {
  return computed(() => {
    const directHost = store.directHost();
    const portFromHostMatch = /(?<=:)\d+$/.exec(directHost);
    const portFromHost = portFromHostMatch ? Number(portFromHostMatch[0]) : null;
    return portFromHost === store.port()
      ? directHost
      : directHost.replace(/(:\d+)$/, '');
  });
}
