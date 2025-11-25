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

export function buildRequirementIsSatisfied(relevantItemCount: readonly number[]): (req: AutopelagoRequirement) => boolean {
  const allItems = BAKED_DEFINITIONS_FULL.allItems;
  const ratCount = relevantItemCount.reduce((acc, val, i) => acc + (val * allItems[i].ratCount), 0);

  function isSatisfied(req: AutopelagoRequirement): boolean {
    if ('item' in req) {
      return relevantItemCount[req.item] >= 1;
    }

    if ('ratCount' in req) {
      return ratCount >= req.ratCount;
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
  'nowhere-useful-to-move', // value is irrelevant, just need to fill the slot
  'nowhere-useful-to-move',
  'closest-reachable-unchecked',
  'user-requested',
  'aura-driven',
  'go-mode',
  'startled',
];

export function determineDesirability(options: Readonly<DetermineDesirabilityOptions>): EnumVal<typeof Desirability>[] {
  const {
    defs: {
      startLocation,
      startRegion,
      allLocations,
      allRegions,
    },
    victoryLocation,
    relevantItemCount,
    locationIsChecked,
    isStartled,
    userRequestedLocations,
    auraDrivenLocations,
  } = options;
  const isSatisfied = buildRequirementIsSatisfied(relevantItemCount);

  // be VERY careful about the ordering of how we fill this array. earlier blocks get overwritten by
  // later blocks that should take precedence.
  const result = Array<EnumVal<typeof Desirability>>(allLocations.length);

  // lowest precedence: checked/unchecked
  for (let i = 0; i < result.length; i++) {
    result[i] = locationIsChecked[i] ? Desirability.CHECKED : Desirability.UNCHECKED;
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

export interface TargetLocationResult {
  readonly location: number;
  readonly reason: TargetLocationReason;
}

export interface DetermineTargetLocationOptions {
  currentLocation: number;
  defs: Readonly<AutopelagoDefinitions>;
  desirability: readonly number[];
}

export function determineTargetLocation(options: Readonly<DetermineTargetLocationOptions>): TargetLocationResult {
  const { currentLocation, defs: { allLocations, allRegions }, desirability } = options;
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

  if (resultDesirability > Desirability.UNCHECKED && resultDesirability < Desirability.STARTLED) {
    // anything "weird" considers locations past soft-locked landmarks. for now, route calculation
    // assumes that you can walk through the entire path without checking landmarks in between, so
    // for now we need to actually target the closest unchecked landmark along the path, if any.
    const prev = allRegions.map(() => ({ r: NaN, d: Infinity }));
    const currentRegion = allLocations[currentLocation].regionLocationKey[0];
    prev[currentRegion].d = 0;
    q.enqueue(currentRegion);
    for (let r = q.dequeue(); r !== undefined; r = q.dequeue()) {
      const d = prev[r].d + 1;
      for (const [r2] of allRegions[r].connected.all) {
        if (prev[r2].d > d && !('loc' in allRegions[r2] && desirability[allRegions[r2].loc] === Desirability.AVOID)) {
          prev[r2].d = d;
          prev[r2].r = r;
          q.enqueue(r2);
        }
      }
    }

    for (let r = allLocations[bestLocation].regionLocationKey[0]; !Number.isNaN(r); r = prev[r].r) {
      const region = allRegions[r];
      if ('loc' in region) {
        if (desirability[region.loc] === Desirability.UNCHECKED) {
          bestLocation = region.loc;
        }
      }
    }
  }

  return {
    location: bestLocation,
    reason: desirabilityMap[resultDesirability],
  };
}

export function targetLocationResultsEqual(a: TargetLocationResult, b: TargetLocationResult): boolean {
  return a.location === b.location && a.reason === b.reason;
}

export interface DetermineRouteOptions {
  currentLocation: number;
  targetLocation: number;
  defs: Readonly<AutopelagoDefinitions>;
  regionIsHardLocked: Readonly<BitArray>;
  regionIsSoftLocked: Readonly<BitArray>;
}

export function determineRoute(options: Readonly<DetermineRouteOptions>): number[] {
  const { currentLocation, targetLocation, defs: { allLocations }, regionIsSoftLocked, regionIsHardLocked } = options;
  const prev = allLocations.map(() => ({ l: NaN, d: Infinity }));
  prev[currentLocation].d = 0;
  const q = new Queue<number>();
  q.enqueue(currentLocation);
  for (let l = q.dequeue(); l !== undefined; l = q.dequeue()) {
    if (l === targetLocation) {
      break;
    }

    const d = prev[l].d + 1;
    for (const [l2] of allLocations[l].connected.all) {
      if (prev[l2].d > d && !regionIsHardLocked[allLocations[l2].regionLocationKey[0]]) {
        prev[l2].d = d;
        prev[l2].l = l;
        if (!regionIsSoftLocked[allLocations[l2].regionLocationKey[0]]) {
          q.enqueue(l2);
        }
      }
    }
  }

  const result: number[] = [];
  for (let l = targetLocation; !Number.isNaN(l); l = prev[l].l) {
    result.push(l);
  }

  return result.reverse();
}
