import { withImmutableState } from '@angular-architects/ngrx-toolkit';
import { signalStoreFeature, withMethods } from '@ngrx/signals';

import type { DefiningGameState } from '../game/defining-state';
import { isDone, toAmortized } from '../game/state-functions';

export const withGameState = <PrngState>() => signalStoreFeature(
  withImmutableState({
    gameState: null as DefiningGameState<PrngState> | null,
  }),
  withMethods(store => ({
    advance: () => {
      const gameState = store.gameState();
      if (!gameState) {
        throw new Error('call init() first');
      }

      if (isDone(gameState)) {
        return;
      }

      const _amortized = toAmortized(gameState);
    },
  })),
);
