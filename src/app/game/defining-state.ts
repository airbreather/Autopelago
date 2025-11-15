import type BitArray from '@bitarray/typedarray';
import { List, Set as ImmutableSet } from 'immutable';
import rand from 'pure-rand';

import type { AutopelagoBuff, AutopelagoTrap, VictoryLocationYamlKey } from '../data/resolved-definitions';
import type { UserRequestedLocation } from '../data/slot-data';
import type { TargetLocationEvidence } from './target-location-evidence';

export interface DefiningGameState {
  // values that don't change throughout the entire run:
  readonly lactoseIntolerant: boolean;
  readonly victoryLocationYamlKey: VictoryLocationYamlKey;
  readonly enabledBuffs: ReadonlySet<AutopelagoBuff>;
  readonly enabledTraps: ReadonlySet<AutopelagoTrap>;
  readonly locationIsProgression: Readonly<BitArray>;
  readonly locationIsTrap: Readonly<BitArray>;

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
  readonly processedReceivedItemCount: number;
  readonly currentLocation: number;
  readonly workDone: number;
  readonly auraDrivenLocations: List<number>;
  readonly userRequestedLocations: List<UserRequestedLocation>;
  readonly previousTargetLocationEvidence: TargetLocationEvidence;

  // other values that are also persisted on the server, but in a different form:
  readonly receivedItems: List<number>;
  readonly checkedLocations: ImmutableSet<number>;

  // other values that still affect how we transition to the next state, but which can reasonably be
  // dropped
  readonly prng: rand.RandomGenerator;

  // other values used to indicate autonomous actions that haven't been observed yet:
  outgoingCheckedLocations: ImmutableSet<number>;
  outgoingMoves: List<readonly [number, number]>;
}
