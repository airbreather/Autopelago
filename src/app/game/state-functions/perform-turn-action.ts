import type { TurnState, TurnStateWithConfirmedTargetLocation } from '../turn-state';
import { setTargetLocation } from './index';

export default function (state: TurnState): TurnStateWithConfirmedTargetLocation {
  if (state.remainingActions <= 0) {
    throw new Error('no actions remaining.');
  }

  if (!state.confirmedTarget) {
    return setTargetLocation(state);
  }

  // just make it terminate
  return {
    ...state,
    remainingActions: state.remainingActions - 1,
  };
}
