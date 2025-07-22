import { computed, effect } from '@angular/core';

import { patchState, signalStore, withComputed, withHooks, withState, withMethods } from '@ngrx/signals';

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

export const ConnectScreenStore = signalStore(
  { providedIn: 'root' },
  withState(() => ({
    ...initialState,
    ...loadFromStorage(),
  })),
  withHooks({
    onInit(store) {
      effect(() => {
        try {
          localStorage.setItem(STORAGE_KEY, JSON.stringify({
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
          }));
        }
        catch {
          // Silently fail if localStorage is not available
        }
      });
    },
  }),
  withComputed(({ sendChatMessages, whenTargetChanges, whenBecomingBlocked, whenStillBlocked, whenBecomingUnblocked, forOneTimeEvents }) => ({
    sendChatMessagesWhenTargetChanges: computed(() => sendChatMessages() && whenTargetChanges()),
    sendChatMessagesWhenBecomingBlocked: computed(() => sendChatMessages() && whenBecomingBlocked()),
    sendChatMessagesWhenStillBlocked: computed(() => sendChatMessages() && whenBecomingBlocked() && whenStillBlocked()),
    sendChatMessagesWhenBecomingUnblocked: computed(() => sendChatMessages() && whenBecomingUnblocked()),
    sendChatMessagesForOneTimeEvents: computed(() => sendChatMessages() && forOneTimeEvents()),
  })),
  withMethods(store => ({
    updateSlot(slot: string) {
      patchState(store, { slot });
    },

    updateDirectHost(directHost: string) {
      const m = /(?<=:)\d+$/.exec(directHost);
      if (m) {
        const port = Number(m[0]);
        patchState(store, { directHost, port });
      }
      else {
        patchState(store, { directHost });
      }
    },

    updatePassword(password: string) {
      patchState(store, { password });
    },

    updateMinTime(minTime: number) {
      patchState(store, { minTime });
    },

    updateMaxTime(maxTime: number) {
      patchState(store, { maxTime });
    },

    updateEnableTileAnimations(enableTileAnimations: boolean) {
      patchState(store, { enableTileAnimations });
    },

    updateEnableRatAnimations(enableRatAnimations: boolean) {
      patchState(store, { enableRatAnimations });
    },

    updateSendChatMessages(sendChatMessages: boolean) {
      patchState(store, { sendChatMessages });
    },

    updateWhenTargetChanges(whenTargetChanges: boolean) {
      patchState(store, { whenTargetChanges });
    },

    updateWhenBecomingBlocked(whenBecomingBlocked: boolean) {
      patchState(store, { whenBecomingBlocked });
    },

    updateWhenStillBlocked(whenStillBlocked: boolean) {
      patchState(store, { whenStillBlocked });
    },

    updateWhenBecomingUnblocked(whenBecomingUnblocked: boolean) {
      patchState(store, { whenBecomingUnblocked });
    },

    updateForOneTimeEvents(forOneTimeEvents: boolean) {
      patchState(store, { forOneTimeEvents });
    },

    updatePort(port: number) {
      patchState(store, { port });
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
