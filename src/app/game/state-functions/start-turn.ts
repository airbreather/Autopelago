import type { DerivedGameState } from '../derived-state';
import type { TurnStateBeforeConfirmingTargetLocation } from '../turn-state';

export default function (state: DerivedGameState): TurnStateBeforeConfirmingTargetLocation {
  const result = {
    ...state,

    // compiler only lets us mutate the explicitly listed properties from DerivedGameState, so:
    foodFactor: state.foodFactor,
    sluggishCarryover: state.sluggishCarryover,
    distractionCounter: state.distractionCounter,

    // add the extra properties for TurnState:
    remainingActions: 3,
    energyBank: 0,
    locationAttempts: 0,
    checkedAnyLocations: false,
    confirmedTarget: false,
    targetLocation: null,
    targetLocationReason: null,
  } satisfies TurnStateBeforeConfirmingTargetLocation;

  if (result.sluggishCarryover) {
    result.remainingActions--;
    result.sluggishCarryover = false;
  }

  if (result.foodFactor < 0) {
    result.remainingActions--;
    result.foodFactor++;
  }
  else if (result.foodFactor > 0) {
    result.remainingActions++;
    result.foodFactor--;
  }

  // positive energyFactor lets the player make up to 2x as much distance in a single round of
  // (only) movement. in the past, this was uncapped, which basically meant that the player
  // would often teleport great distances, which was against the spirit of the whole thing.
  result.energyBank = result.remainingActions;

  if (result.distractionCounter > 0) {
    // being startled takes priority over a distraction. you just saw a ghost, you're not
    // thinking about the Rubik's Cube that you got at about the same time!
    if (result.startledCounter === 0) {
      result.remainingActions = 0;
    }

    result.distractionCounter--;
  }

  return result;
}
