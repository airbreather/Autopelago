import { withImmutableState } from '@angular-architects/ngrx-toolkit';
import { patchState, signalStoreFeature, withMethods } from '@ngrx/signals';

import type { DefiningGameState } from '../game/defining-state';
import { endTurn, isDone, performTurnAction, startTurn } from '../game/state-functions';
import derive from '../game/state-functions/derive';
import type { TurnState } from '../game/turn-state';

export function withGameState() {
  return signalStoreFeature(
    withImmutableState({
      gameState: null as DefiningGameState | null,
    }),
    withMethods(store => ({
      advance() {
        const gameState = store.gameState();
        if (!gameState) {
          throw new Error('call init() first');
        }

        if (isDone(gameState)) {
          return;
        }

        let turnState: TurnState = startTurn(derive(gameState));
        while (turnState.remainingActions > 0) {
          turnState = performTurnAction(turnState);
        }

        patchState(store, { gameState: endTurn(turnState) });
      },
    })),
  );
}
