import { withImmutableState } from '@angular-architects/ngrx-toolkit';
import { computed } from '@angular/core';
import type { ParamMap } from '@angular/router';

import { patchState, signalStore, withComputed, withMethods } from '@ngrx/signals';
import {
  CONNECT_SCREEN_STATE_DEFAULTS,
  connectScreenStateFromQueryParams,
} from '../connect-screen/connect-screen-state';

export const ConnectScreenStore = signalStore(
  { providedIn: 'root' },
  withImmutableState(CONNECT_SCREEN_STATE_DEFAULTS),
  withComputed(store => ({
    sendChatMessagesWhenTargetChanges: computed(() => store.sendChatMessages() && store.whenTargetChanges()),
    sendChatMessagesWhenBecomingBlocked: computed(() => store.sendChatMessages() && store.whenBecomingBlocked()),
    sendChatMessagesWhenStillBlocked: computed(() => store.sendChatMessages() && store.whenBecomingBlocked() && store.whenStillBlocked()),
    sendChatMessagesWhenBecomingUnblocked: computed(() => store.sendChatMessages() && store.whenBecomingUnblocked()),
    sendChatMessagesForOneTimeEvents: computed(() => store.sendChatMessages() && store.forOneTimeEvents()),
  })),
  withMethods(store => ({
    initFromQueryParams(qp: ParamMap) {
      patchState(store, connectScreenStateFromQueryParams(qp));
    },
  })),
);
