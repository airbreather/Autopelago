import BitArray from '@bitarray/typedarray';
import Queue from 'yocto-queue';

import { AutopelagoDefinitions } from '../data/resolved-definitions';

export interface DetermineTargetLocationOptions {
  currentLocation: number;
  defs: Readonly<AutopelagoDefinitions>;
  desirability: readonly number[];
}

export interface DetermineRouteOptions {
  currentLocation: number;
  targetLocation: number;
  isStartled: boolean;
  defs: Readonly<AutopelagoDefinitions>;
  regionIsLocked: Readonly<BitArray>;
  locationIsChecked: Readonly<BitArray>;
}

export type TargetLocationReason =
  'game-not-started'
  | 'nowhere-useful-to-move'
  | 'closest-reachable-unchecked'
  | 'user-requested'
  | 'aura-driven'
  | 'go-mode'
  | 'startled';

export const Desirability = {
  STARTLED: 6,
  GO_MODE: 5,
  AURA_DRIVEN: 4,
  USER_REQUESTED: 3,
  UNCHECKED: 2,
  CHECKED: 1,
  AVOID: 0,
} as const;

const desirabilityMap: TargetLocationReason[] = [
  'game-not-started',
  'nowhere-useful-to-move',
  'closest-reachable-unchecked',
  'user-requested',
  'aura-driven',
  'go-mode',
  'startled',
];

export interface TargetLocationResult {
  location: number;
  reason: TargetLocationReason;
}

export function determineTargetLocation(options: Readonly<DetermineTargetLocationOptions>): TargetLocationResult {
  const { currentLocation, defs: { allLocations }, desirability } = options;
  let bestLocation = currentLocation;
  let resultDesirability = -1;
  const visited = new BitArray(allLocations.length);
  const q = new Queue<number>();
  function tryEnqueue(loc: number) {
    if (!visited[loc]) {
      q.enqueue(loc);
      visited[loc] = 1;
    }
  }
  tryEnqueue(currentLocation);
  for (let loc = q.dequeue(); loc !== undefined; loc = q.dequeue()) {
    const d = desirability[loc];
    if (d > resultDesirability) {
      bestLocation = loc;
      resultDesirability = d;
    }

    for (const [nxt] of allLocations[loc].connected.all) {
      if (desirability[nxt] > 0) {
        tryEnqueue(nxt);
      }
    }
  }

  return {
    location: bestLocation,
    reason: desirabilityMap[resultDesirability],
  };
}

export function determineRoute(options: Readonly<DetermineRouteOptions>): number[] {
  return [...new Set([options.currentLocation, options.targetLocation])];
}
