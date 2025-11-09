import { withImmutableState, withStorageSync } from '@angular-architects/ngrx-toolkit';
import { computed } from '@angular/core';
import type { ParamMap } from '@angular/router';

import { patchState, signalStore, withComputed, withMethods } from '@ngrx/signals';

type ShortQueryParameterNames =
  | 'h'
  | 'p'
  | 's'
  | 'w'
  | 't'
  | 'T'
  | 'A'
  | 'a'
  | 'c'
  | 'z'
  | 'b'
  | 'B'
  | 'u'
  | 'o'
  ;

type QueryParamNameBidirectionalMap =
  & Record<keyof typeof initialState, ShortQueryParameterNames>
  & Record<ShortQueryParameterNames, keyof typeof initialState>
  ;

const QUERY_PARAM_NAME_MAP = {
  host: 'h',
  port: 'p',
  slot: 's',
  password: 'w',
  minTime: 't',
  maxTime: 'T',
  enableTileAnimations: 'A',
  enableRatAnimations: 'a',
  sendChatMessages: 'c',
  whenTargetChanges: 'z',
  whenBecomingBlocked: 'b',
  whenStillBlocked: 'B',
  whenBecomingUnblocked: 'u',
  forOneTimeEvents: 'o',
  h: 'host',
  p: 'port',
  s: 'slot',
  w: 'password',
  t: 'minTime',
  T: 'maxTime',
  A: 'enableTileAnimations',
  a: 'enableRatAnimations',
  c: 'sendChatMessages',
  z: 'whenTargetChanges',
  b: 'whenBecomingBlocked',
  B: 'whenStillBlocked',
  u: 'whenBecomingUnblocked',
  o: 'forOneTimeEvents',
} as const satisfies QueryParamNameBidirectionalMap;

type QueryParams = {
  [K in keyof typeof initialState as typeof QUERY_PARAM_NAME_MAP[K]]: typeof initialState[K] extends boolean ? 0 | 1 : typeof initialState[K];
};

// Default state
const initialState = {
  slot: '',
  host: 'archipelago.gg',
  port: 38281,
  password: '' as string | null,
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

export const ConnectScreenStore = signalStore(
  { providedIn: 'root' },
  withImmutableState(initialState),
  withStorageSync(STORAGE_KEY),
  withComputed(store => ({
    sendChatMessagesWhenTargetChanges: computed(() => store.sendChatMessages() && store.whenTargetChanges()),
    sendChatMessagesWhenBecomingBlocked: computed(() => store.sendChatMessages() && store.whenBecomingBlocked()),
    sendChatMessagesWhenStillBlocked: computed(() => store.sendChatMessages() && store.whenBecomingBlocked() && store.whenStillBlocked()),
    sendChatMessagesWhenBecomingUnblocked: computed(() => store.sendChatMessages() && store.whenBecomingUnblocked()),
    sendChatMessagesForOneTimeEvents: computed(() => store.sendChatMessages() && store.forOneTimeEvents()),
    queryParams: computed<QueryParams>(() => ({
      [QUERY_PARAM_NAME_MAP.host]: store.host(),
      [QUERY_PARAM_NAME_MAP.port]: store.port(),
      [QUERY_PARAM_NAME_MAP.slot]: store.slot(),
      [QUERY_PARAM_NAME_MAP.password]: store.password(),
      [QUERY_PARAM_NAME_MAP.minTime]: store.minTime(),
      [QUERY_PARAM_NAME_MAP.maxTime]: store.maxTime(),
      [QUERY_PARAM_NAME_MAP.enableTileAnimations]: store.enableTileAnimations() ? 1 : 0,
      [QUERY_PARAM_NAME_MAP.enableRatAnimations]: store.enableRatAnimations() ? 1 : 0,
      [QUERY_PARAM_NAME_MAP.sendChatMessages]: store.sendChatMessages() ? 1 : 0,
      [QUERY_PARAM_NAME_MAP.whenTargetChanges]: store.whenTargetChanges() ? 1 : 0,
      [QUERY_PARAM_NAME_MAP.whenBecomingBlocked]: store.whenBecomingBlocked() ? 1 : 0,
      [QUERY_PARAM_NAME_MAP.whenStillBlocked]: store.whenStillBlocked() ? 1 : 0,
      [QUERY_PARAM_NAME_MAP.whenBecomingUnblocked]: store.whenBecomingUnblocked() ? 1 : 0,
      [QUERY_PARAM_NAME_MAP.forOneTimeEvents]: store.forOneTimeEvents() ? 1 : 0,
    })),
  })),
  withMethods(store => ({
    initFromQueryParams(qp: ParamMap) {
      const slot = qp.get(QUERY_PARAM_NAME_MAP.slot);
      const host = qp.get(QUERY_PARAM_NAME_MAP.host);
      const port = Number(qp.get(QUERY_PARAM_NAME_MAP.port));
      if (!(slot && host && port)) {
        throw new Error(`Missing required query params. host (${QUERY_PARAM_NAME_MAP.host}), port (${QUERY_PARAM_NAME_MAP.port}), and slot (${QUERY_PARAM_NAME_MAP.slot}) must be provided!`);
      }

      patchState(store, {
        slot,
        host,
        port,
        password: qp.get(QUERY_PARAM_NAME_MAP.password),
        minTime: Number(qp.get(QUERY_PARAM_NAME_MAP.minTime)) || initialState.minTime,
        maxTime: Number(qp.get(QUERY_PARAM_NAME_MAP.maxTime)) || initialState.maxTime,
        enableTileAnimations: Boolean(qp.get(QUERY_PARAM_NAME_MAP.enableTileAnimations) ?? initialState.enableTileAnimations),
        enableRatAnimations: Boolean(qp.get(QUERY_PARAM_NAME_MAP.enableRatAnimations) ?? initialState.enableRatAnimations),
        sendChatMessages: Boolean(qp.get(QUERY_PARAM_NAME_MAP.sendChatMessages) ?? initialState.sendChatMessages),
        whenTargetChanges: Boolean(qp.get(QUERY_PARAM_NAME_MAP.whenTargetChanges) ?? initialState.whenTargetChanges),
        whenBecomingBlocked: Boolean(qp.get(QUERY_PARAM_NAME_MAP.whenBecomingBlocked) ?? initialState.whenBecomingBlocked),
        whenStillBlocked: Boolean(qp.get(QUERY_PARAM_NAME_MAP.whenStillBlocked) ?? initialState.whenStillBlocked),
        whenBecomingUnblocked: Boolean(qp.get(QUERY_PARAM_NAME_MAP.whenBecomingUnblocked) ?? initialState.whenBecomingUnblocked),
        forOneTimeEvents: Boolean(qp.get(QUERY_PARAM_NAME_MAP.forOneTimeEvents) ?? initialState.forOneTimeEvents),
      });
    },

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
