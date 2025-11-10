import { List } from 'immutable';
import rand from 'pure-rand';

import type { AutopelagoBuff, AutopelagoTrap, VictoryLocationYamlKey } from '../data/resolved-definitions';
import type { DerivedGameState } from './derived-state';
import type { LocationEvidence } from './location-evidence';

export interface UserRequestedLocation {
  location: number;
  userName: string;
}

export interface DefiningGameState {
  // values that don't change throughout the entire run:
  readonly victoryLocationYamlKey: VictoryLocationYamlKey;
  readonly enabledBuffs: ReadonlySet<AutopelagoBuff>;
  readonly enabledTraps: ReadonlySet<AutopelagoTrap>;

  // other values that get persisted on the server:
  readonly foodFactor: number;
  readonly luckFactor: number;
  readonly energyFactor: number;
  readonly styleFactor: number;
  readonly distractionCounter: number;
  readonly startledCounter: number;
  readonly hasConfidence: boolean;
  readonly mercyFactor: number;
  readonly sluggishCarryover: boolean;
  readonly currentLocation: number;
  readonly auraDrivenLocations: List<number>;
  readonly userRequestedLocations: List<Readonly<UserRequestedLocation>>;
  readonly previousLocationEvidence: LocationEvidence;

  // other values that are also persisted on the server, but in a different form:
  readonly receivedItems: List<number>;
  readonly checkedLocations: List<number>;

  // other values that still affect how we transition to the next state, but which can reasonably be
  // dropped
  readonly prng: rand.RandomGenerator;

  // intentionally omitted: anything that can be computed from the above values.
  // for that, see derived-state.ts.
}

export function extractLocationEvidence(gameState: DerivedGameState): LocationEvidence {
  const {
    defs: { allRegions },
    startledCounter,
    auraDrivenLocations,
    userRequestedLocations,
    regionIsHardLocked,
  } = gameState;

  if (startledCounter > 0) {
    return { startled: true };
  }

  const firstAuraDrivenLocation = auraDrivenLocations.first() ?? null;
  if (firstAuraDrivenLocation !== null) {
    return { startled: false, firstAuraDrivenLocation };
  }

  const clearedOrClearableLandmarks = List(
    allRegions
      .filter((region, i) => 'loc' in region && !regionIsHardLocked[i])
      .map((_region, i) => i),
  );

  if (userRequestedLocations.size > 0) {
    return {
      startled: false,
      firstAuraDrivenLocation: null,
      clearedOrClearableLandmarks: List(clearedOrClearableLandmarks),
      userRequestedLocations: userRequestedLocations,
    };
  }

  return {
    startled: false,
    firstAuraDrivenLocation: null,
    clearedOrClearableLandmarks: List(clearedOrClearableLandmarks),
    userRequestedLocations: null,
  };
}
