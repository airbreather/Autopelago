import type BitArray from '@bitarray/typedarray';
import { List, Set as ImmutableSet } from 'immutable';
import type { RandomGenerator } from 'pure-rand/types/RandomGenerator';

import type { AutopelagoUniqueItemKey } from '../data/items';
import type { AutopelagoAura, VictoryLocationYamlKey } from '../data/resolved-definitions';
import type { UserRequestedLocation } from '../data/slot-data';
import type { Weighted } from '../utils/weighted-sampler';
import type { TargetLocationEvidence } from './target-location-evidence';

export interface MovementAction {
  type: 'move';
  fromLocation: number;
  toLocation: number;
}

export interface CheckLocationsAction {
  type: 'check-locations';
  locations: readonly number[];
}

export interface DeathAction {
  type: 'death';
  cause: 'just-poisoned' | 'death-link';
}

export interface UWinAction {
  type: 'u-win';
}

export type AnimatableAction =
  | MovementAction
  | CheckLocationsAction
  | DeathAction
  | UWinAction
  ;

export interface DefiningGameState {
  // values that don't change throughout the entire run:
  readonly lactoseIntolerant: boolean;
  readonly victoryLocationYamlKey: VictoryLocationYamlKey;
  readonly uniqueItemsByNetworkId: ReadonlyMap<number, AutopelagoUniqueItemKey>;
  readonly aurasGrantedByItemNetworkId: ReadonlyMap<number, readonly AutopelagoAura[]>;
  readonly ratCountByItemNetworkId: ReadonlyMap<number, number>;
  readonly locationIsProgression: Readonly<BitArray>;
  readonly locationIsTrap: Readonly<BitArray>;
  readonly messagesForChangedTarget: readonly Weighted<string>[];
  readonly messagesForEnterGoMode: readonly Weighted<string>[];
  readonly messagesForEnterBK: readonly Weighted<string>[];
  readonly messagesForRemindBK: readonly Weighted<string>[];
  readonly messagesForExitBK: readonly Weighted<string>[];
  readonly messagesForCompletedGoal: readonly Weighted<string>[];
  readonly messagesForImpendingDoom: readonly Weighted<string>[] | null;
  readonly sendDeathLink: boolean;
  readonly deathDelaySeconds: number;

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
  readonly hyperFocusLocation: number | null;
  readonly auraDrivenLocations: List<number>;
  readonly userRequestedLocations: List<UserRequestedLocation>;
  readonly previousTargetLocationEvidence: TargetLocationEvidence;

  // other values that are also persisted on the server, but in a different form:
  readonly receivedItemNetworkIds: List<number>;
  readonly checkedLocations: ImmutableSet<number>;

  // other values that still affect how we transition to the next state, but which can reasonably be
  // dropped
  readonly prng: RandomGenerator;

  // other values used to indicate autonomous actions that haven't been observed yet:
  readonly outgoingCheckedLocations: List<number>;
  readonly outgoingAnimatableActions: List<AnimatableAction>;
  readonly outgoingMessages: List<string>;
  readonly outgoingAuraDrivenLocations: List<number>;
  readonly outgoingDeathCause: string | null;
  readonly impendingDoom: boolean;
}
