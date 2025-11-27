import BitArray from '@bitarray/typedarray';
import Queue from 'yocto-queue';

import {
  type AutopelagoDefinitions,
  type AutopelagoRegion,
  type AutopelagoRequirement,
  BAKED_DEFINITIONS_FULL,
} from '../data/resolved-definitions';
import type { UserRequestedLocation } from '../data/slot-data';
import type { EnumVal } from '../utils/types';

export function buildRequirementIsSatisfied(relevantItemCount: readonly number[], allLocationsAreChecked: boolean): (req: AutopelagoRequirement) => boolean {
  const allItems = BAKED_DEFINITIONS_FULL.allItems;
  const ratCount = relevantItemCount.reduce((acc, val, i) => acc + (val * allItems[i].ratCount), 0);

  function isSatisfied(req: AutopelagoRequirement): boolean {
    if ('item' in req) {
      return relevantItemCount[req.item] >= 1;
    }

    if ('ratCount' in req) {
      return ratCount >= req.ratCount;
    }

    if ('fullClear' in req) {
      return allLocationsAreChecked;
    }

    const minRequired = req.minRequired === 'all' ? req.children.length : req.minRequired;
    return req.children.reduce((acc, req) => acc + (isSatisfied(req) ? 1 : 0), 0) >= minRequired;
  }

  return isSatisfied;
}

export interface DetermineDesirabilityOptions {
  defs: Readonly<AutopelagoDefinitions>;
  victoryLocation: number;
  relevantItemCount: readonly number[];
  locationIsChecked: Readonly<BitArray>;
  isStartled: boolean;
  userRequestedLocations: Iterable<UserRequestedLocation>;
  auraDrivenLocations: Iterable<number>;
}

export type TargetLocationReason =
  | 'nowhere-useful-to-move'
  | 'closest-reachable-unchecked'
  | 'user-requested'
  | 'aura-driven'
  | 'go-mode'
  | 'game-over'
  | 'startled';

export const Desirability = {
  STARTLED: 7,
  GAME_OVER: 6,
  GO_MODE: 5,
  AURA_DRIVEN: 4,
  USER_REQUESTED: 3,
  UNCHECKED: 2,
  CHECKED: 1,
  AVOID: 0,
} as const;

const desirabilityMap: TargetLocationReason[] = [
  'nowhere-useful-to-move', // value is irrelevant, just need to fill the slot
  'nowhere-useful-to-move',
  'closest-reachable-unchecked',
  'user-requested',
  'aura-driven',
  'go-mode',
  'game-over',
  'startled',
];

export function determineDesirability(options: Readonly<DetermineDesirabilityOptions>): EnumVal<typeof Desirability>[] {
  const {
    defs: {
      startLocation,
      startRegion,
      allLocations,
      allRegions,
      moonCommaThe,
    },
    victoryLocation,
    relevantItemCount,
    locationIsChecked,
    isStartled,
    userRequestedLocations,
    auraDrivenLocations,
  } = options;
  const isSatisfied = buildRequirementIsSatisfied(relevantItemCount, !!(moonCommaThe !== null && locationIsChecked[moonCommaThe.location]));

  // be VERY careful about the ordering of how we fill this array. earlier blocks get overwritten by
  // later blocks that should take precedence.
  const result = Array<EnumVal<typeof Desirability>>(allLocations.length);

  // lowest precedence: checked/unchecked
  let allLocationsMightBeChecked: 1 | 0 = 1;
  for (let i = 0; i < result.length; i++) {
    result[i] = locationIsChecked[i] ? Desirability.CHECKED : Desirability.UNCHECKED;
    if (i !== moonCommaThe?.location) {
      allLocationsMightBeChecked &&= locationIsChecked[i];
    }
  }

  if (allLocationsMightBeChecked) {
    result[moonCommaThe?.location ?? victoryLocation] = Desirability.GAME_OVER;
    return result;
  }

  // next precedence: user requested
  for (const { location } of userRequestedLocations) {
    result[location] = Desirability.USER_REQUESTED;
  }

  // next precedence: aura driven
  for (const location of auraDrivenLocations) {
    result[location] = Desirability.AURA_DRIVEN;
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
      if (!isSatisfied(region.requirement) || (isStartled && !locationIsChecked[region.loc])) {
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

export interface TargetLocationResult {
  readonly location: number;
  readonly reason: TargetLocationReason;
  readonly path: readonly number[];
}

export interface DetermineTargetLocationOptions {
  currentLocation: number;
  defs: Readonly<AutopelagoDefinitions>;
  desirability: readonly number[];
}

export function determineTargetLocation(options: Readonly<DetermineTargetLocationOptions>): TargetLocationResult {
  const { currentLocation, defs: { allLocations, regionForLandmarkLocation }, desirability } = options;
  let bestLocation = currentLocation;
  let resultDesirability = -1;
  const prev = allLocations.map(() => ({ l: NaN, d: Infinity }));
  prev[currentLocation].d = 0;
  const q = new Queue<number>();
  q.enqueue(currentLocation);
  for (let loc = q.dequeue(); loc !== undefined; loc = q.dequeue()) {
    const des = desirability[loc];
    if (des > resultDesirability) {
      bestLocation = loc;
      resultDesirability = des;
    }

    const d = prev[loc].d + 1;
    for (const [nxt] of allLocations[loc].connected.all) {
      if (prev[nxt].d > d && desirability[nxt] > Desirability.AVOID) {
        prev[nxt].d = d;
        prev[nxt].l = loc;
        q.enqueue(nxt);
      }
    }
  }

  const path: number[] = [];
  for (let loc = bestLocation; !Number.isNaN(loc); loc = prev[loc].l) {
    // anything "weird" considers locations past soft-locked landmarks. for now, route calculation
    // assumes that you can walk through the entire path without checking landmarks in between, so
    // for now we need to actually target the closest unchecked landmark along the path, if any.
    if (bestLocation !== loc && !Number.isNaN(regionForLandmarkLocation[loc]) && desirability[loc] === Desirability.UNCHECKED) {
      path.length = 0;
      bestLocation = loc;
    }
    path.push(loc);
  }

  return {
    location: bestLocation,
    reason: desirabilityMap[resultDesirability],
    path: path.reverse(),
  };
}

export function targetLocationResultsEqual(a: TargetLocationResult, b: TargetLocationResult): boolean {
  return a.location === b.location && a.reason === b.reason;
}
