import { type DefiningGameState, extractLocationEvidence } from '../defining-state';
import type { TurnState } from '../turn-state';

function extractDefiningProps(state: DefiningGameState): DefiningGameState {
  return {
    victoryLocationYamlKey: state.victoryLocationYamlKey,
    enabledBuffs: state.enabledBuffs,
    enabledTraps: state.enabledTraps,
    foodFactor: state.foodFactor,
    luckFactor: state.luckFactor,
    energyFactor: state.energyFactor,
    styleFactor: state.styleFactor,
    distractionCounter: state.distractionCounter,
    startledCounter: state.startledCounter,
    hasConfidence: state.hasConfidence,
    mercyFactor: state.mercyFactor,
    sluggishCarryover: state.sluggishCarryover,
    currentLocation: state.currentLocation,
    auraDrivenLocations: state.auraDrivenLocations,
    userRequestedLocations: state.userRequestedLocations,
    previousLocationEvidence: state.previousLocationEvidence,
    receivedItems: state.receivedItems,
    checkedLocations: state.checkedLocations,
    prng: state.prng,
  };
}

export default function (state: TurnState): DefiningGameState {
  const startledCounter = Math.max(0, state.startledCounter - 1);
  const bumpMercyFactor = !(state.checkedAnyLocations || state.locationAttempts === 0);
  return {
    ...extractDefiningProps(state),
    sluggishCarryover: state.remainingActions < 0,
    startledCounter,
    mercyFactor: state.mercyFactor + (bumpMercyFactor ? 1 : 0),
    previousLocationEvidence: extractLocationEvidence(state),
  };
}
