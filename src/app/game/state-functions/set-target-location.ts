import BitArray from '@bitarray/typedarray';
import Queue from 'yocto-queue';
import { extractLocationEvidence } from '../defining-state';
import { locationEvidenceEquals } from '../location-evidence';
import type { TurnState, TurnStateWithConfirmedTargetLocation } from '../turn-state';

function calculateTargetLocation(state: TurnState): Pick<TurnStateWithConfirmedTargetLocation, 'targetLocation' | 'targetLocationReason'> {
  if (state.startledCounter > 0) {
    return {
      targetLocation: state.defs.startLocation,
      targetLocationReason: 'startled',
    };
  }

  const victoryLocation = state.defs.victoryLocationsByYamlKey.get(state.victoryLocationYamlKey);
  if (!(victoryLocation === undefined || state.regionIsHardLocked[victoryLocation] || state.locationIsChecked[victoryLocation])) {
    return {
      targetLocation: victoryLocation,
      targetLocationReason: 'go-mode',
    };
  }

  const firstAuraDrivenLocation = state.auraDrivenLocations.first();
  if (firstAuraDrivenLocation !== undefined) {
    return {
      targetLocation: firstAuraDrivenLocation,
      targetLocationReason: 'aura-driven',
    };
  }

  for (const { location } of state.userRequestedLocations) {
    if (!state.regionIsHardLocked[state.defs.allLocations[location].regionLocationKey[0]]) {
      return {
        targetLocation: location,
        targetLocationReason: 'user-requested',
      };
    }
  }

  const closestUncheckedLocation = findClosestUncheckedLocation(state);
  if (closestUncheckedLocation !== null) {
    return {
      targetLocation: closestUncheckedLocation,
      targetLocationReason: 'closest-reachable-unchecked',
    };
  }

  return {
    targetLocation: state.currentLocation,
    targetLocationReason: 'nowhere-useful-to-move',
  };
}

function findClosestUncheckedLocation(state: TurnState): number | null {
  const { defs: { allLocations }, currentLocation, locationIsChecked } = state;
  const visited = new BitArray(allLocations.length);
  const q = new Queue<number>();

  function tryEnqueue(l: number) {
    if (!visited[l]) {
      visited[l] = 1;
      q.enqueue(l);
    }
  }

  tryEnqueue(currentLocation);
  for (let l = q.dequeue(); l !== undefined; l = q.dequeue()) {
    const location = allLocations[l];
    if (!locationIsChecked[l]) {
      return l;
    }

    for (const [nxt] of location.connected.all) {
      tryEnqueue(nxt);
    }
  }

  return null;
}

export default function (state: TurnState): TurnStateWithConfirmedTargetLocation {
  let remainingActions = state.remainingActions;
  if (!state.confirmedTarget) {
    const previousEvidence = state.previousLocationEvidence;
    const currentEvidence = extractLocationEvidence(state);
    if (!locationEvidenceEquals(previousEvidence, currentEvidence)) {
      --remainingActions;
    }
  }

  return {
    ...state,
    remainingActions,
    confirmedTarget: true,
    ...calculateTargetLocation(state),
  };
}
