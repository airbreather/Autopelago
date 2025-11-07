import { List } from 'immutable';
import type { AutopelagoBuff, AutopelagoTrap, VictoryLocationYamlKey } from '../data/resolved-definitions';
import type { PreviousLocationEvidence } from './previous-location-evidence';

export interface UserRequestedLocation {
  location: number;
  userName: string;
}

export interface DefiningGameState<PrngState> {
  // values that don't change throughout the entire run:
  victoryLocationYamlKey: VictoryLocationYamlKey;
  enabledBuffs: ReadonlySet<AutopelagoBuff>;
  enabledTraps: ReadonlySet<AutopelagoTrap>;
  lactoseIntolerant: boolean;

  // other values that control what we do step-by-step:
  foodFactor: number;
  luckFactor: number;
  energyFactor: number;
  styleFactor: number;
  distractionCounter: number;
  startledCounter: number;
  hasConfidence: boolean;
  mercyFactor: number;
  sluggishCarryover: boolean;
  currentLocation: number;
  receivedItems: List<number>;
  checkedLocations: List<number>;
  auraDrivenLocations: List<number>;
  userRequestedLocations: List<Readonly<UserRequestedLocation>>;
  prng: PrngState;
  previousLocationEvidence: PreviousLocationEvidence;

  // intentionally omitted: anything that can be computed from the above values.
}
