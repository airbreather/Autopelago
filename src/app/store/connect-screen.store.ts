import { computed, effect } from '@angular/core';
import { patchState, signalStore, withComputed, withHooks, withMethods, withState } from '@ngrx/signals';

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

// Local storage key
const STORAGE_KEY = 'autopelago-connect-screen-state';

// Helper functions for local storage
function loadFromStorage(): Partial<typeof initialState> {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored ? JSON.parse(stored) as Partial<typeof initialState> : {};
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
            host: store.host(),
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

    updateHost(directHost: string) {
      const m = /(?<=:)\d+$/.exec(directHost);
      if (m) {
        const port = Number(m[0]);
        patchState(store, { host: directHost, port });
      }
      else {
        patchState(store, { host: directHost });
      }
    },

    updatePort(port: number) {
      patchState(store, ({ host }) => ({
        host: Number.isInteger(port)
          ? host.replace(/(?<=:)\d+$/, port.toString())
          : host.replace(/:\d+$/, ''),
        port,
      }));
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
  })),
);
