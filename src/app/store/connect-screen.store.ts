import { computed, effect, Injectable, signal } from '@angular/core';

// Default state
const initialState = {
  slot: '',
  host: 'archipelago.gg',
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

@Injectable({ providedIn: 'root' })
export class ConnectScreenStoreService {
  // State signals
  readonly #slot = signal(initialState.slot);
  readonly #host = signal(initialState.host);
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
  readonly host = this.#host.asReadonly();
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
    // Local storage key
    const STORAGE_KEY = 'autopelago-connect-screen-state';
    try {
      const storedJSON = localStorage.getItem(STORAGE_KEY);
      if (storedJSON) {
        const stored = JSON.parse(storedJSON) as unknown;
        if (stored && typeof stored === 'object') {
          if ('slot' in stored && typeof stored.slot === 'string') {
            this.#slot.set(stored.slot);
          }
          if ('host' in stored && typeof stored.host === 'string') {
            this.#host.set(stored.host);
          }
          if ('port' in stored && typeof stored.port === 'number') {
            this.#port.set(stored.port);
          }
          if ('password' in stored && typeof stored.password === 'string') {
            this.#password.set(stored.password);
          }
          if ('minTime' in stored && typeof stored.minTime === 'number') {
            this.#minTime.set(stored.minTime);
          }
          if ('maxTime' in stored && typeof stored.maxTime === 'number') {
            this.#maxTime.set(stored.maxTime);
          }
          if ('enableTileAnimations' in stored && typeof stored.enableTileAnimations === 'boolean') {
            this.#enableTileAnimations.set(stored.enableTileAnimations);
          }
          if ('enableRatAnimations' in stored && typeof stored.enableRatAnimations === 'boolean') {
            this.#enableRatAnimations.set(stored.enableRatAnimations);
          }
          if ('sendChatMessages' in stored && typeof stored.sendChatMessages === 'boolean') {
            this.#sendChatMessages.set(stored.sendChatMessages);
          }
          if ('whenTargetChanges' in stored && typeof stored.whenTargetChanges === 'boolean') {
            this.#whenTargetChanges.set(stored.whenTargetChanges);
          }
          if ('whenBecomingBlocked' in stored && typeof stored.whenBecomingBlocked === 'boolean') {
            this.#whenBecomingBlocked.set(stored.whenBecomingBlocked);
          }
          if ('whenStillBlocked' in stored && typeof stored.whenStillBlocked === 'boolean') {
            this.#whenStillBlocked.set(stored.whenStillBlocked);
          }
          if ('whenBecomingUnblocked' in stored && typeof stored.whenBecomingUnblocked === 'boolean') {
            this.#whenBecomingUnblocked.set(stored.whenBecomingUnblocked);
          }
          if ('forOneTimeEvents' in stored && typeof stored.forOneTimeEvents === 'boolean') {
            this.#forOneTimeEvents.set(stored.forOneTimeEvents);
          }
        }
      }
    }
    catch {
      // Silently fail if localStorage is not available
    }

    // Auto-save to localStorage
    effect(() => {
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify({
          slot: this.slot(),
          host: this.host(),
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

  updateHost(directHost: string) {
    this.#host.set(directHost);
    const m = /(?<=:)\d+$/.exec(directHost);
    if (m) {
      const port = Number(m[0]);
      this.#port.set(port);
    }
  }

  updatePort(port: number) {
    if (Number.isInteger(port)) {
      this.#host.update(h => h.replace(/(?<=:)\d+$/, port.toString()));
    }
    else {
      this.#host.update(h => h.replace(/:\d+$/, ''));
    }

    this.#port.set(port);
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
}
