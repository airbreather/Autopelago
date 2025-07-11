import { signalStore, withState, withMethods, patchState } from '@ngrx/signals';
import { computed } from '@angular/core';

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
  port: 65535,
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
  } catch {
    return {};
  }
}

function saveToStorage(state: ConnectScreenState): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch {
    // Silently fail if localStorage is not available
  }
}

// Interface for the store parameter within withMethods (only has state signals, not methods)
interface StateSignals {
  slot: () => string;
  directHost: () => string;
  port: () => number;
  password: () => string;
  minTime: () => number;
  maxTime: () => number;
  enableTileAnimations: () => boolean;
  enableRatAnimations: () => boolean;
  sendChatMessages: () => boolean;
  whenTargetChanges: () => boolean;
  whenBecomingBlocked: () => boolean;
  whenStillBlocked: () => boolean;
  whenBecomingUnblocked: () => boolean;
  forOneTimeEvents: () => boolean;
}

function getCurrentState(store: StateSignals): ConnectScreenState {
  return {
    slot: store.slot(),
    directHost: store.directHost(),
    port: store.port(),
    password: store.password(),
    minTime: store.minTime(),
    maxTime: store.maxTime(),
    enableTileAnimations: store.enableTileAnimations(),
    enableRatAnimations: store.enableRatAnimations(),
    sendChatMessages: store.sendChatMessages(),
    whenTargetChanges: store.whenTargetChanges(),
    whenBecomingBlocked: store.whenBecomingBlocked(),
    whenStillBlocked: store.whenStillBlocked(),
    whenBecomingUnblocked: store.whenBecomingUnblocked(),
    forOneTimeEvents: store.forOneTimeEvents(),
  };
}

export const ConnectScreenStore = signalStore(
  { providedIn: 'root' },
  withState(() => ({
    ...initialState,
    ...loadFromStorage(),
  })),
  withMethods((store) => ({
    updateSlot(slot: string) {
      patchState(store, { slot });
      saveToStorage(getCurrentState(store));
    },

    updateDirectHost(directHost: string) {
      const m = /(?<=:)\d+$/.exec(directHost);
      if (m) {
        const port = Number(m[0]);
        patchState(store, { directHost, port });
      } else {
        patchState(store, { directHost });
      }
      saveToStorage(getCurrentState(store));
    },

    updatePassword(password: string) {
      patchState(store, { password });
      saveToStorage(getCurrentState(store));
    },

    updateMinTime(minTime: number) {
      patchState(store, { minTime });
      saveToStorage(getCurrentState(store));
    },

    updateMaxTime(maxTime: number) {
      patchState(store, { maxTime });
      saveToStorage(getCurrentState(store));
    },

    updateEnableTileAnimations(enableTileAnimations: boolean) {
      patchState(store, { enableTileAnimations });
      saveToStorage(getCurrentState(store));
    },

    updateEnableRatAnimations(enableRatAnimations: boolean) {
      patchState(store, { enableRatAnimations });
      saveToStorage(getCurrentState(store));
    },

    updateSendChatMessages(sendChatMessages: boolean) {
      patchState(store, { sendChatMessages });
      saveToStorage(getCurrentState(store));
    },

    updateWhenTargetChanges(whenTargetChanges: boolean) {
      patchState(store, { whenTargetChanges });
      saveToStorage(getCurrentState(store));
    },

    updateWhenBecomingBlocked(whenBecomingBlocked: boolean) {
      patchState(store, { whenBecomingBlocked });
      saveToStorage(getCurrentState(store));
    },

    updateWhenStillBlocked(whenStillBlocked: boolean) {
      patchState(store, { whenStillBlocked });
      saveToStorage(getCurrentState(store));
    },

    updateWhenBecomingUnblocked(whenBecomingUnblocked: boolean) {
      patchState(store, { whenBecomingUnblocked });
      saveToStorage(getCurrentState(store));
    },

    updateForOneTimeEvents(forOneTimeEvents: boolean) {
      patchState(store, { forOneTimeEvents });
      saveToStorage(getCurrentState(store));
    },

    updatePort(port: number) {
      patchState(store, { port });
      saveToStorage(getCurrentState(store));
    },
  })),
);

export function createHostSelector(store: InstanceType<typeof ConnectScreenStore>) {
  return computed(() => {
    const directHost = store.directHost();
    const portFromHostMatch = /(?<=:)\d+$/.exec(directHost);
    const portFromHost = portFromHostMatch ? Number(portFromHostMatch[0]) : null;
    return portFromHost === store.port()
        ? directHost
        : directHost.replace(/(:\d+)$/, '');
  });
}
