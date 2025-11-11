import BitArray from '@bitarray/typedarray';
import { List } from 'immutable';
import Queue from 'yocto-queue';
import type { DerivedGameState } from '../derived-state';
import { type TargetLocationEvidence, targetLocationEvidenceEquals } from '../target-location-evidence';
import type { TurnState, TurnStateWithConfirmedTargetLocation } from '../turn-state';

function extractLocationEvidence(gameState: DerivedGameState): TargetLocationEvidence {
  const {
    defs: { allRegions },
    startledCounter,
    auraDrivenLocations,
    userRequestedLocations,
    regionIsHardLocked,
  } = gameState;

  if (startledCounter > 0) {
    return { isStartled: true };
  }

  const firstAuraDrivenLocation = auraDrivenLocations.first() ?? null;
  if (firstAuraDrivenLocation !== null) {
    return { isStartled: false, firstAuraDrivenLocation };
  }

  const clearedOrClearableLandmarks = List(
    allRegions
      .filter((region, i) => 'loc' in region && !regionIsHardLocked[i])
      .map((_region, i) => i),
  );

  if (userRequestedLocations.size > 0) {
    return {
      isStartled: false,
      firstAuraDrivenLocation: null,
      clearedOrClearableLandmarks: List(clearedOrClearableLandmarks),
      userRequestedLocations: userRequestedLocations,
    };
  }

  return {
    isStartled: false,
    firstAuraDrivenLocation: null,
    clearedOrClearableLandmarks: List(clearedOrClearableLandmarks),
    userRequestedLocations: null,
  };
}

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
  const previousEvidence = state.previousTargetLocationEvidence;
  const currentEvidence = extractLocationEvidence(state);
  const evidenceChanged = !targetLocationEvidenceEquals(previousEvidence, currentEvidence);
  let remainingActions = state.remainingActions;
  if (state.confirmedTarget) {
    if (!evidenceChanged) {
      return state;
    }
  }
  else if (!evidenceChanged) {
    --remainingActions;
  }

  return {
    ...state,
    remainingActions,
    previousTargetLocationEvidence: currentEvidence,
    confirmedTarget: true,
    ...calculateTargetLocation(state),
  };
}
