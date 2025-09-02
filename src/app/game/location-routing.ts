import BitArray from '@bitarray/typedarray';
import Queue from 'yocto-queue';

import type { AutopelagoDefinitions, AutopelagoRegion, AutopelagoRequirement } from '../data/resolved-definitions';
import type { EnumVal } from '../util';

export interface DetermineDesirabilityOptions {
  defs: Readonly<AutopelagoDefinitions>;
  victoryLocation: number;
  itemCount: readonly number[];
  locationIsChecked: Readonly<BitArray>;
  isStartled: boolean;
  userRequestedLocation: number | null;
  auraDrivenLocation: number | null;
}

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

export function determineDesirability(options: Readonly<DetermineDesirabilityOptions>): EnumVal<typeof Desirability>[] {
  const {
    defs: {
      startLocation,
      startRegion,
      allLocations,
      allRegions,
      allItems,
    },
    victoryLocation,
    itemCount,
    locationIsChecked,
    isStartled,
    userRequestedLocation,
    auraDrivenLocation,
  } = options;
  const ratCount = itemCount.reduce((acc, val, i) => acc + (val * allItems[i].ratCount), 0);
  function isSatisfied(req: AutopelagoRequirement): boolean {
    if ('item' in req) {
      return itemCount[req.item] >= 1;
    }

    if ('ratCount' in req) {
      return ratCount >= req.ratCount;
    }

    const minRequired = req.minRequired === 'all' ? req.children.length : req.minRequired;
    return req.children.reduce((acc, req) => acc + (isSatisfied(req) ? 1 : 0), 0) >= minRequired;
  }

  // be VERY careful about the ordering of how we fill this array. earlier blocks get overwritten by
  // later blocks that should take precedence.
  const result = Array<EnumVal<typeof Desirability>>(allLocations.length);

  // lowest precedence: checked/unchecked
  for (let i = 0; i < result.length; i++) {
    result[i] = locationIsChecked[i] ? Desirability.CHECKED : Desirability.UNCHECKED;
  }

  // next precedence: user requested
  if (userRequestedLocation !== null) {
    result[userRequestedLocation] = Desirability.USER_REQUESTED;
  }

  // next precedence: aura driven
  if (auraDrivenLocation !== null) {
    result[auraDrivenLocation] = Desirability.AURA_DRIVEN;
  }

  // now address the landmarks by walking to each from the start region. determineTargetLocation
  // never passes through an AVOID, so for any landmark whose requirement is not satisfied, we only
  // need to mark it as AVOID. and if we can make it to the victory location, then we're in go mode.
  const visited = new BitArray(allRegions.length);
  const q = new Queue<AutopelagoRegion>();
  function tryEnqueue(r: number) {
    if (!visited[r]) {
      q.enqueue(allRegions[r]);
      visited[r] = 1;
    }
  }
  tryEnqueue(startRegion);
  for (let region = q.dequeue(); region !== undefined; region = q.dequeue()) {
    if ('loc' in region) {
      if (!isSatisfied(region.requirement)) {
        result[region.loc] = Desirability.AVOID;
        continue;
      }

      if (region.loc === victoryLocation && !locationIsChecked[region.loc]) {
        result[region.loc] = Desirability.GO_MODE;
      }
    }

    for (const [nxt] of region.connected.all) {
      tryEnqueue(nxt);
    }
  }

  if (isStartled) {
    result[startLocation] = Desirability.STARTLED;
  }

  return result;
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
      if (desirability[nxt] > Desirability.AVOID) {
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
