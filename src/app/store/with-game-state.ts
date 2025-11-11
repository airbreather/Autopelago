import { computed } from '@angular/core';
import BitArray from '@bitarray/typedarray';
import { signalStoreFeature, withComputed, withState } from '@ngrx/signals';
import { itemClassifications } from 'archipelago.js';
import { List, Set as ImmutableSet } from 'immutable';
import rand from 'pure-rand';
import Queue from 'yocto-queue';
import {
  type AutopelagoAura,
  type AutopelagoItem,
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
} from '../data/resolved-definitions';
import type { AutopelagoStoredData } from '../data/slot-data';
import type { DefiningGameState, UserRequestedLocation } from '../game/defining-state';
import {
  buildRequirementIsSatisfied,
  Desirability,
  determineDesirability,
  determineTargetLocation,
  type TargetLocationResult,
  targetLocationResultsEqual,
} from '../game/location-routing';

import {
  type TargetLocationEvidence,
  targetLocationEvidenceEquals,
  targetLocationEvidenceToJSONSerializable,
} from '../game/target-location-evidence';
import type { EnumVal } from '../util';

function arraysEqual(a: readonly number[], b: readonly number[]) {
  return a.length === b.length && a.every((v, i) => v === b[i]);
}

function bitArraysEqual(a: Readonly<BitArray>, b: Readonly<BitArray>) {
  if (a.length !== b.length) {
    return false;
  }
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) {
      return false;
    }
  }
  return true;
}

function regionLocksEqual(a: RegionLocks, b: RegionLocks) {
  return bitArraysEqual(a.regionIsSoftLocked, b.regionIsSoftLocked)
    && bitArraysEqual(a.regionIsHardLocked, b.regionIsHardLocked)
    && bitArraysEqual(a.landmarkRegionIsSoftLocked, b.landmarkRegionIsSoftLocked)
    && bitArraysEqual(a.landmarkRegionIsHardLocked, b.landmarkRegionIsHardLocked);
}

interface RegionLocks {
  readonly regionIsHardLocked: Readonly<BitArray>;
  readonly regionIsSoftLocked: Readonly<BitArray>;
  readonly landmarkRegionIsHardLocked: Readonly<BitArray>;
  readonly landmarkRegionIsSoftLocked: Readonly<BitArray>;
}

export interface ResolvedItem extends AutopelagoItem {
  lactoseAwareName: string;
  enabledAurasGranted: readonly AutopelagoAura[];
}

const initialState: DefiningGameState = {
  lactoseIntolerant: false,
  victoryLocationYamlKey: 'snakes_on_a_planet',
  enabledBuffs: new Set(),
  enabledTraps: new Set(),
  locationIsProgression: new BitArray(0),
  locationIsTrap: new BitArray(0),
  foodFactor: NaN,
  luckFactor: NaN,
  energyFactor: NaN,
  styleFactor: NaN,
  distractionCounter: NaN,
  startledCounter: NaN,
  hasConfidence: false,
  mercyFactor: NaN,
  sluggishCarryover: false,
  processedReceivedItemCount: NaN,
  currentLocation: -1,
  workDone: NaN,
  auraDrivenLocations: List<number>(),
  userRequestedLocations: List<Readonly<UserRequestedLocation>>(),
  previousTargetLocationEvidence: null,
  receivedItems: List<number>(),
  checkedLocations: ImmutableSet<number>(),
  prng: rand.xoroshiro128plus(42),
};

export function withGameState() {
  return signalStoreFeature(
    withState(initialState),
    withComputed((store) => {
      const defs = computed(() => BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[store.victoryLocationYamlKey()]);
      const isStartled = computed(() => store.startledCounter() > 0);
      const locationIsChecked = computed<Readonly<BitArray>>(() => {
        const { allLocations } = defs();
        const locationIsChecked = new BitArray(allLocations.length);
        for (const loc of store.checkedLocations()) {
          locationIsChecked[loc] = 1;
        }
        return locationIsChecked;
      });
      const receivedItemCountLookup = computed<readonly number[]>(() => {
        const { allItems } = defs();
        const result = Array<number>(allItems.length).fill(0);
        for (const i of store.receivedItems()) {
          ++result[i];
        }
        return result;
      });
      const ratCount = computed<number>(() => {
        const { allItems } = defs();
        let ratCount = 0;
        for (const item of store.receivedItems()) {
          ratCount += allItems[item].ratCount;
        }
        return ratCount;
      });
      const resolvedItems = computed<readonly ResolvedItem[]>(() => {
        const { allItems } = defs();
        const lactoseIntolerant = store.lactoseIntolerant();
        const enabledAuras = new Set([...store.enabledBuffs(), ...store.enabledTraps()]);
        return allItems.map(item => ({
          ...item,
          lactoseAwareName: lactoseIntolerant ? item.lactoseIntolerantName : item.lactoseName,
          enabledAurasGranted: item.aurasGranted.filter(a => enabledAuras.has(a)),
        }));
      });
      const victoryLocation = computed(() => {
        const { victoryLocationsByYamlKey } = defs();
        const victoryLocationYamlKey = store.victoryLocationYamlKey();
        const victoryLocation = victoryLocationsByYamlKey.get(victoryLocationYamlKey);
        if (victoryLocation === undefined) {
          throw new Error(`unknown victory location: ${victoryLocationYamlKey}. this is a programming error!`);
        }
        return victoryLocation;
      });
      const _relevantItemCountLookup = computed<readonly number[]>(() => {
        const { allItems } = defs();
        const result = Array<number>(allItems.length).fill(0);
        for (const i of store.receivedItems()) {
          const item = allItems[i];
          if (item.ratCount > 0 || ((item.flags & itemClassifications.progression) === itemClassifications.progression)) {
            ++result[i];
          }
        }

        return result;
      }, { equal: arraysEqual });
      const _regionLocks = computed<RegionLocks>(() => {
        const { allRegions, startRegion } = defs();
        const locationIsChecked_ = locationIsChecked();
        const regionIsHardLocked = new BitArray(allRegions.length);
        const regionIsSoftLocked = new BitArray(allRegions.length);
        const landmarkRegionIsHardLocked = new BitArray(allRegions.length);
        const landmarkRegionIsSoftLocked = new BitArray(allRegions.length);
        for (let i = 0; i < allRegions.length; i++) {
          regionIsHardLocked[i] = 1;
          regionIsSoftLocked[i] = 1;
          landmarkRegionIsHardLocked[i] = 1;
          landmarkRegionIsSoftLocked[i] = 1;
        }
        const isSatisfied = buildRequirementIsSatisfied(_relevantItemCountLookup());
        const visited = new BitArray(allRegions.length);
        const q = new Queue<number>();

        function tryEnqueue(r: number) {
          if (!visited[r]) {
            q.enqueue(r);
            visited[r] = 1;
          }
        }

        tryEnqueue(startRegion);
        for (let r = q.dequeue(); r !== undefined; r = q.dequeue()) {
          regionIsHardLocked[r] = 0;

          const region = allRegions[r];
          if ('loc' in region) {
            landmarkRegionIsHardLocked[r] = 0;
            if (!isSatisfied(region.requirement)) {
              continue;
            }
            if (locationIsChecked_[region.loc]) {
              regionIsSoftLocked[r] = 0;
              landmarkRegionIsSoftLocked[r] = 0;
            }
          }
          else {
            regionIsSoftLocked[r] = 0;
          }
          regionIsHardLocked[r] = 0;
          for (const [r] of region.connected.all) {
            tryEnqueue(r);
          }
        }
        return {
          get regionIsHardLocked() { return regionIsHardLocked; },
          get regionIsSoftLocked() { return regionIsSoftLocked; },
          get landmarkRegionIsHardLocked() { return landmarkRegionIsHardLocked; },
          get landmarkRegionIsSoftLocked() { return landmarkRegionIsSoftLocked; },
        };
      }, { equal: regionLocksEqual });
      const _clearedOrClearableLandmarks = computed<readonly number[]>(() => {
        const { landmarkRegionIsHardLocked } = _regionLocks();
        const result: number[] = [];
        for (let i = 0; i < landmarkRegionIsHardLocked.length; i++) {
          if (landmarkRegionIsHardLocked[i]) {
            result.push(i);
          }
        }
        return result;
      }, { equal: arraysEqual });
      const _desirability = computed<readonly EnumVal<typeof Desirability>[]>(() =>
        determineDesirability({
          defs: defs(),
          victoryLocation: victoryLocation(),
          relevantItemCount: _relevantItemCountLookup(),
          locationIsChecked: locationIsChecked(),
          isStartled: isStartled(),
          userRequestedLocations: store.userRequestedLocations(),
          auraDrivenLocations: store.auraDrivenLocations(),
        }), { equal: arraysEqual });
      const targetLocationEvidence = computed<TargetLocationEvidence>(() => {
        if (isStartled()) {
          return { isStartled: true } satisfies TargetLocationEvidence;
        }

        const firstAuraDrivenLocation = store.auraDrivenLocations().first() ?? null;
        if (firstAuraDrivenLocation !== null) {
          return { isStartled: false, firstAuraDrivenLocation } satisfies TargetLocationEvidence;
        }
        const clearedOrClearableLandmarks = List(_clearedOrClearableLandmarks());
        const userRequestedLocations = store.userRequestedLocations();
        return userRequestedLocations.size === 0
          ? { isStartled: false, firstAuraDrivenLocation, clearedOrClearableLandmarks, userRequestedLocations: null }
          : { isStartled: false, firstAuraDrivenLocation, clearedOrClearableLandmarks, userRequestedLocations };
      }, { equal: targetLocationEvidenceEquals });
      const _targetLocationDetail = computed<TargetLocationResult>(() => {
        return determineTargetLocation({
          defs: defs(),
          desirability: _desirability(),
          currentLocation: store.currentLocation(),
        });
      }, { equal: targetLocationResultsEqual });
      const targetLocation = computed(() => {
        return _targetLocationDetail().location;
      });
      const targetLocationReason = computed(() => {
        return _targetLocationDetail().reason;
      });
      const asStoredData = computed<AutopelagoStoredData>(() => ({
        foodFactor: store.foodFactor(),
        luckFactor: store.luckFactor(),
        energyFactor: store.energyFactor(),
        styleFactor: store.styleFactor(),
        distractionCounter: store.distractionCounter(),
        startledCounter: store.startledCounter(),
        hasConfidence: store.hasConfidence(),
        mercyFactor: store.mercyFactor(),
        sluggishCarryover: store.sluggishCarryover(),
        processedReceivedItemCount: store.processedReceivedItemCount(),
        currentLocation: store.currentLocation(),
        workDone: store.workDone(),
        auraDrivenLocations: store.auraDrivenLocations().toJS(),
        userRequestedLocations: store.userRequestedLocations().toJS().map(l => ({
          location: l.location,
          userName: l.userName,
        })),
        previousTargetLocationEvidence: targetLocationEvidenceToJSONSerializable(store.previousTargetLocationEvidence()),
      }));

      return {
        defs,
        isStartled,
        locationIsChecked,
        receivedItemCountLookup,
        ratCount,
        resolvedItems,
        victoryLocation,
        _relevantItemCountLookup,
        _regionLocks,
        _clearedOrClearableLandmarks,
        _desirability,
        targetLocationEvidence,
        _targetLocationDetail,
        targetLocation,
        targetLocationReason,
        asStoredData,
      };
    }),
  );
}
