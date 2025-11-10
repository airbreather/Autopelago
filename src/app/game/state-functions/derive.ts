import BitArray from '@bitarray/typedarray';
import { List } from 'immutable';
import Queue from 'yocto-queue';
import { type AutopelagoRequirement, BAKED_DEFINITIONS_BY_VICTORY_LANDMARK } from '../../data/resolved-definitions';
import type { DefiningGameState } from '../defining-state';
import type { DerivedGameState } from '../derived-state';

export default function (gameState: DefiningGameState): DerivedGameState {
  const defs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[gameState.victoryLocationYamlKey];

  const receivedItemCountLookup = Array<number>(defs.allItems.length).fill(0);
  for (const item of gameState.receivedItems) {
    receivedItemCountLookup[item]++;
  }

  let ratCount = 0;
  for (const item of defs.itemsWithNonzeroRatCounts) {
    ratCount += receivedItemCountLookup[item] * defs.allItems[item].ratCount;
  }

  function isSatisfied(req: AutopelagoRequirement): boolean {
    if ('item' in req) {
      return receivedItemCountLookup[req.item] >= 1;
    }

    if ('ratCount' in req) {
      return ratCount >= req.ratCount;
    }

    const minRequired = req.minRequired === 'all' ? req.children.length : req.minRequired;
    return req.children.reduce((acc, req) => acc + (isSatisfied(req) ? 1 : 0), 0) >= minRequired;
  }

  const locationIsChecked = new BitArray(defs.allLocations.length);
  for (const location of gameState.checkedLocations) {
    locationIsChecked[location] = 1;
  }

  const regionIsHardLocked = new BitArray(defs.allRegions.length);
  const regionIsSoftLocked = new BitArray(defs.allRegions.length);
  for (let i = 0; i < defs.allRegions.length; i++) {
    regionIsHardLocked[i] = 1;
    regionIsSoftLocked[i] = 1;
  }

  const visited = new BitArray(defs.allRegions.length);
  const q = new Queue<number>();

  function tryEnqueue(r: number) {
    if (!visited[r]) {
      q.enqueue(r);
      visited[r] = 1;
    }
  }

  tryEnqueue(defs.startRegion);
  for (let r = q.dequeue(); r !== undefined; r = q.dequeue()) {
    const region = defs.allRegions[r];
    if ('loc' in region) {
      if (locationIsChecked[region.loc]) {
        regionIsSoftLocked[r] = 0;
      }
      else if (!isSatisfied(region.requirement)) {
        continue;
      }

      regionIsHardLocked[r] = 0;
    }
    else {
      regionIsHardLocked[r] = 0;
      regionIsSoftLocked[r] = 0;
    }

    for (const [nxt] of region.connected.all) {
      tryEnqueue(nxt);
    }
  }

  return {
    ...gameState,
    defs,
    regionIsHardLocked,
    regionIsSoftLocked,
    locationIsChecked,
    receivedItemCountLookup: List(receivedItemCountLookup),
    ratCount,
  } satisfies DerivedGameState;
}
