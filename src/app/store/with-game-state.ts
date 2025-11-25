import { computed, effect, untracked } from '@angular/core';
import BitArray from '@bitarray/typedarray';
import { patchState, signalStoreFeature, withComputed, withHooks, withMethods, withState } from '@ngrx/signals';
import { itemClassifications, PlayersManager } from 'archipelago.js';
import { List, Set as ImmutableSet } from 'immutable';
import rand from 'pure-rand';
import Queue from 'yocto-queue';
import type { Message } from '../archipelago-client';
import {
  type AutopelagoAura,
  type AutopelagoItem,
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
} from '../data/resolved-definitions';
import type { AutopelagoStoredData, UserRequestedLocation } from '../data/slot-data';
import type { DefiningGameState } from '../game/defining-state';
import {
  buildRequirementIsSatisfied,
  Desirability,
  determineDesirability,
  determineRoute,
  determineTargetLocation,
  type TargetLocationResult,
  targetLocationResultsEqual,
} from '../game/location-routing';
import { parseCommand } from '../game/parse-command';

import {
  type TargetLocationEvidence,
  targetLocationEvidenceEquals,
  targetLocationEvidenceToJSONSerializable,
} from '../game/target-location-evidence';
import { type EnumVal, type Mutable } from '../util';
import { createWeightedSampler } from '../weighted-sampler';

const importMeta = import.meta as (ImportMeta & { env?: Partial<Record<string, unknown>> });
const IS_VITEST = ('env' in importMeta) && ('VITEST' in importMeta.env);

function arraysEqual(a: readonly number[], b: readonly number[]) {
  if (a === b) {
    return true;
  }

  return a.length === b.length && a.every((v, i) => v === b[i]);
}

function bitArraysEqual(a: Readonly<BitArray>, b: Readonly<BitArray>) {
  if (a === b) {
    return true;
  }
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
  if (a === b) {
    return true;
  }

  return bitArraysEqual(a.regionIsSoftLocked, b.regionIsSoftLocked)
    && bitArraysEqual(a.regionIsHardLocked, b.regionIsHardLocked)
    && bitArraysEqual(a.regionIsLandmarkAndNotHardLocked, b.regionIsLandmarkAndNotHardLocked)
    && bitArraysEqual(a.regionIsLandmarkAndNotSoftLocked, b.regionIsLandmarkAndNotSoftLocked);
}

interface ModifyRollOptions {
  d20: number;
  ratCount: number;
  mercy: number;
  multi: number;
  unlucky: boolean;
  stylish: boolean;
}
function modifyRoll({ d20, ratCount, mercy, multi, unlucky, stylish }: ModifyRollOptions) {
  return d20 + getPermanentRollModifier(ratCount) + mercy + (multi * -5) + (unlucky ? -5 : 0) + (stylish ? 5 : 0);
}

function getPermanentRollModifier(ratCount: number) {
  // diminishing returns
  let rolling = 0;

  // +1 for every 3 rats up to the first 12
  if (ratCount <= 12) {
    rolling += Math.floor(ratCount / 3);
    return rolling;
  }

  rolling += 4;
  ratCount -= 12;

  // beyond that, +1 for every 5 rats up to the next 15
  if (ratCount <= 15) {
    rolling += Math.floor(ratCount / 5);
    return rolling;
  }

  rolling += 3;
  ratCount -= 15;

  // beyond that, +1 for every 7 rats up to the next 14
  if (ratCount <= 14) {
    rolling += Math.floor(ratCount / 7);
    return rolling;
  }

  rolling += 2;
  ratCount -= 14;

  // everything else is +1 for every 8 rats.
  rolling += Math.floor(ratCount / 8);
  return rolling;
}

interface RegionLocks {
  readonly regionIsHardLocked: Readonly<BitArray>;
  readonly regionIsSoftLocked: Readonly<BitArray>;
  readonly regionIsLandmarkAndNotHardLocked: Readonly<BitArray>;
  readonly regionIsLandmarkAndNotSoftLocked: Readonly<BitArray>;
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
  messagesForChangedTarget: [],
  messagesForEnterGoMode: [],
  messagesForEnterBK: [],
  messagesForRemindBK: [],
  messagesForExitBK: [],
  messagesForCompletedGoal: [],
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
  auraDrivenLocations: List<number>(),
  processedAuraDrivenLocationCount: 0,
  userRequestedLocations: List<Readonly<UserRequestedLocation>>(),
  previousTargetLocationEvidence: {
    isStartled: false,
    userRequestedLocations: null,
    firstAuraDrivenLocation: null,
    clearedOrClearableLandmarks: List<number>(),
  },
  receivedItems: List<number>(),
  checkedLocations: ImmutableSet<number>(),
  prng: rand.xoroshiro128plus(42),
  outgoingCheckedLocations: List<number>(),
  outgoingMoves: List<readonly [number, number]>(),
  outgoingMessages: List<string>(),
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
      const regionIsLandmarkWithUnsatisfiedRequirement = computed<Readonly<BitArray>>(() => {
        const isSatisfied = buildRequirementIsSatisfied(_relevantItemCountLookup());
        const { allRegions } = defs();
        const regionIsLandmarkWithUnsatisfiedRequirement = new BitArray(allRegions.length);
        for (let i = 0; i < allRegions.length; i++) {
          const region = allRegions[i];
          if ('loc' in region && !isSatisfied(region.requirement)) {
            regionIsLandmarkWithUnsatisfiedRequirement[i] = 1;
          }
        }
        return regionIsLandmarkWithUnsatisfiedRequirement;
      });
      const _regionLocks = computed<RegionLocks>(() => {
        const { allRegions, startRegion } = defs();
        const locationIsChecked_ = locationIsChecked();
        const regionIsLandmarkWithUnsatisfiedRequirement_ = regionIsLandmarkWithUnsatisfiedRequirement();
        const regionIsHardLocked = new BitArray(allRegions.length);
        const regionIsSoftLocked = new BitArray(allRegions.length);
        const regionIsLandmarkAndNotHardLocked = new BitArray(allRegions.length);
        const regionIsLandmarkAndNotSoftLocked = new BitArray(allRegions.length);
        for (let i = 0; i < allRegions.length; i++) {
          regionIsHardLocked[i] = 1;
          regionIsSoftLocked[i] = 1;
        }
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
          if (regionIsLandmarkWithUnsatisfiedRequirement_[r]) {
            continue;
          }
          const region = allRegions[r];
          if ('loc' in region) {
            regionIsLandmarkAndNotHardLocked[r] = 1;
            if (locationIsChecked_[region.loc]) {
              regionIsSoftLocked[r] = 0;
              regionIsLandmarkAndNotSoftLocked[r] = 1;
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
          get regionIsLandmarkAndNotHardLocked() { return regionIsLandmarkAndNotHardLocked; },
          get regionIsLandmarkAndNotSoftLocked() { return regionIsLandmarkAndNotSoftLocked; },
        };
      }, { equal: regionLocksEqual });
      const _clearedOrClearableLandmarks = computed<readonly number[]>(() => {
        const { regionIsLandmarkAndNotHardLocked } = _regionLocks();
        const result: number[] = [];
        for (let i = 0; i < regionIsLandmarkAndNotHardLocked.length; i++) {
          if (regionIsLandmarkAndNotHardLocked[i]) {
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
      const targetLocationChosenBecauseSmart = computed(() => {
        return targetLocationReason() === 'aura-driven' && store.locationIsProgression()[targetLocation()];
      });
      const targetLocationChosenBecauseConspiratorial = computed(() => {
        return targetLocationReason() === 'aura-driven' && store.locationIsTrap()[targetLocation()];
      });
      const targetLocationRoute = computed(() => {
        return determineRoute({
          // when ONLY the current location changes, that's just because the player is traversing
          // the route that we calculated. this is expensive enough that it's preferable to have our
          // callers have to look for where their current location is along the route each time.
          currentLocation: untracked(() => store.currentLocation()),
          targetLocation: targetLocation(),
          defs: defs(),
          regionIsHardLocked: isStartled() ? _regionLocks().regionIsSoftLocked : _regionLocks().regionIsHardLocked,
          regionIsSoftLocked: _regionLocks().regionIsSoftLocked,
        });
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
        auraDrivenLocations: store.auraDrivenLocations().toJS(),
        processedAuraDrivenLocationCount: store.processedAuraDrivenLocationCount(),
        userRequestedLocations: store.userRequestedLocations().toJS().map(l => ({
          location: l.location,
          userSlot: l.userSlot,
        })),
        previousTargetLocationEvidence: targetLocationEvidenceToJSONSerializable(store.previousTargetLocationEvidence()),
      }));
      const sampleMessage = computed(() => ({
        forChangedTarget: createWeightedSampler(store.messagesForChangedTarget()),
        forEnterGoMode: createWeightedSampler(store.messagesForEnterGoMode()),
        forEnterBK: createWeightedSampler(store.messagesForEnterBK()),
        forRemindBK: createWeightedSampler(store.messagesForRemindBK()),
        forExitBK: createWeightedSampler(store.messagesForExitBK()),
        forCompletedGoal: createWeightedSampler(store.messagesForCompletedGoal()),
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
        regionIsLandmarkWithUnsatisfiedRequirement,
        _regionLocks,
        _clearedOrClearableLandmarks,
        _desirability,
        targetLocationEvidence,
        _targetLocationDetail,
        targetLocation,
        targetLocationReason,
        targetLocationChosenBecauseSmart,
        targetLocationChosenBecauseConspiratorial,
        targetLocationRoute,
        asStoredData,
        sampleMessage,
      };
    }),
    withMethods((store) => {
      interface AlreadyAuraDriven {
        kind: 'already-aura-driven';
      }
      interface AlreadyRequested {
        kind: 'already-requested';
        userSlots: readonly number[];
      }
      interface NewlyAdded {
        kind: 'newly-added';
      }
      type AddUserRequestedLocationResult = AlreadyAuraDriven | AlreadyRequested | NewlyAdded;
      function addUserRequestedLocation(userSlot: number, location: number): AddUserRequestedLocationResult {
        if (store.auraDrivenLocations().includes(location)) {
          return { kind: 'already-aura-driven' };
        }

        const alreadyRequested = store.userRequestedLocations().filter(l => l.location === location);
        if (alreadyRequested.size > 0 && alreadyRequested.some(l => l.userSlot === userSlot)) {
          return { kind: 'already-requested', userSlots: [...alreadyRequested.map(l => l.userSlot)] };
        }

        patchState(store, ({ userRequestedLocations }) => ({
          userRequestedLocations: userRequestedLocations.push({ location, userSlot }),
        }));
        return alreadyRequested.size > 0
          ? { kind: 'already-requested', userSlots: [...alreadyRequested.map(l => l.userSlot)] }
          : { kind: 'newly-added' };
      }
      function getMessageForAddUserRequestedLocationResult(loc: number, requestingSlotNumber: number, result: AddUserRequestedLocationResult, probablyPlayerAlias: string, players: PlayersManager) {
        const { allLocations } = store.defs();
        // get the true name of it
        const exactLocName = allLocations[loc].name;
        if (result.kind === 'already-aura-driven') {
          return store.locationIsProgression()[loc]
            ? `Don't worry, ${probablyPlayerAlias}, a little bird already told me ALL about ${exactLocName}.`
            : `Don't worry, ${probablyPlayerAlias}, I'm already on my way to ${exactLocName} after getting an anonymous tip about it.`;
        }

        if (result.kind === 'already-requested') {
          if (result.userSlots.includes(requestingSlotNumber)) {
            return `Hey, ${probablyPlayerAlias}, no worries, I remember ${exactLocName} from when you asked me before.`;
          }

          const firstRequestingUser = result.userSlots[0] === 0
            ? '[Server]'
            : players.teams[players.self.team][result.userSlots[0]].alias;
          return `OK, I'll prioritize ${exactLocName} for you, ${probablyPlayerAlias}. Just so you know, ${firstRequestingUser} already asked me to go there first, but I'll remember that you want me to go there too, in case they change their mind.`;
        }

        if (store._regionLocks().regionIsHardLocked[allLocations[loc].regionLocationKey[0]]) {
          return `I'll keep it in mind that ${exactLocName} is important to you, ${probablyPlayerAlias}. I can't get there just yet, though, so please be patient with me...`;
        }

        if (store.locationIsChecked()[loc]) {
          return `OK, ${probablyPlayerAlias}, I'll go back to ${exactLocName}. I've already sent out its item before, but I trust you!`;
        }

        return `OK, I'll prioritize ${exactLocName} for you, ${probablyPlayerAlias}!`;
      }
      interface NotRequested {
        kind: 'not-requested';
      }
      interface OnlyRequestedForOthers {
        kind: 'only-requested-for-others';
        otherUserSlots: readonly number[];
      }
      interface RemovedPartial {
        kind: 'removed-partial';
        otherUserSlots: readonly number[];
      }
      interface RemovedOnly {
        kind: 'removed-only';
      }
      type RemoveUserRequestedLocationResult = NotRequested | OnlyRequestedForOthers | RemovedPartial | RemovedOnly;
      function removeUserRequestedLocation(userSlot: number, location: number): RemoveUserRequestedLocationResult {
        const alreadyRequested = store.userRequestedLocations().filter(l => l.location === location);
        if (alreadyRequested.size === 0) {
          return { kind: 'not-requested' };
        }

        const toDelete = new Set(userSlot === 0
          ? alreadyRequested
          : alreadyRequested.filter(l => l.userSlot === userSlot));
        if (toDelete.size === 0) {
          return { kind: 'only-requested-for-others', otherUserSlots: [...alreadyRequested.map(l => l.userSlot)] };
        }

        patchState(store, ({ userRequestedLocations }) => ({
          userRequestedLocations: userRequestedLocations.filter(l => !toDelete.has(l)),
        }));
        return toDelete.size === alreadyRequested.size
          ? { kind: 'removed-only' }
          : { kind: 'removed-partial', otherUserSlots: [...alreadyRequested.filter(l => !toDelete.has(l)).map(l => l.userSlot)] };
      }
      function getMessageForRemoveUserRequestedLocationResult(loc: number, requestingSlotNumber: number, result: RemoveUserRequestedLocationResult, probablyPlayerAlias: string, players: PlayersManager) {
        const { allLocations } = store.defs();
        // get the true name of it
        const exactLocName = allLocations[loc].name;
        if (result.kind === 'not-requested') {
          return `OK, ${probablyPlayerAlias}, I won't prioritize ${exactLocName}, but... I already wasn't, so your command didn't do anything.`;
        }

        if (result.kind === 'removed-only') {
          return `OK, ${probablyPlayerAlias}, I won't prioritize ${exactLocName}${requestingSlotNumber === 0 ? '' : ' for you'} anymore.`;
        }

        if (result.kind === 'removed-partial') {
          const playersToReport = result.otherUserSlots.length === 1
            ? result.otherUserSlots[0] === 0
              ? '[Server]'
              : players.teams[players.self.team][result.otherUserSlots[0]].alias
            : result.otherUserSlots.includes(0)
              ? result.otherUserSlots.length === 2
                ? '[Server] and 1 other player'
                : `[Server] and ${(result.otherUserSlots.length - 1).toString()} other players`
              : result.otherUserSlots.length === 1
                ? players.teams[players.self.team][result.otherUserSlots[0]].alias
                : `${result.otherUserSlots.length.toString()} other players`;
          return `OK, ${probablyPlayerAlias}, I won't prioritize ${exactLocName} for you anymore. Just so you know, ${playersToReport} also wanted me to go there, so it's still on my list.`;
        }

        const playersToReport = result.otherUserSlots.length === 1
          ? result.otherUserSlots[0] === 0
            ? '[Server]'
            : players.teams[players.self.team][result.otherUserSlots[0]].alias
          : `${result.otherUserSlots.length.toString()} other players`;
        return `Hey, ${probablyPlayerAlias}, I can't de-prioritize ${exactLocName} for you, because it wasn't requested by you, only by ${playersToReport}.`;
      }
      return {
        addUserRequestedLocation,
        receiveItems(items: Iterable<number>) {
          const { allItems, allLocations } = store.defs();
          patchState(store, (prev) => {
            const result = {
              foodFactor: prev.foodFactor,
              luckFactor: prev.luckFactor,
              energyFactor: prev.energyFactor,
              styleFactor: prev.styleFactor,
              distractionCounter: prev.distractionCounter,
              startledCounter: prev.startledCounter,
              hasConfidence: prev.hasConfidence,
              // the remainder will be clobbered. just helping TypeScript.
              receivedItems: prev.receivedItems,
              processedReceivedItemCount: prev.processedReceivedItemCount,
              auraDrivenLocations: prev.auraDrivenLocations,
              userRequestedLocations: prev.userRequestedLocations,
            } satisfies Partial<DefiningGameState>;
            result.auraDrivenLocations = result.auraDrivenLocations.withMutations((a) => {
              result.receivedItems = prev.receivedItems.withMutations((r) => {
                const locs = allLocations;
                const validProgressionItems = new BitArray(prev.locationIsProgression);
                const validTrapItems = new BitArray(prev.locationIsTrap);
                for (const loc of [...prev.checkedLocations, ...prev.auraDrivenLocations]) {
                  validProgressionItems[loc] = 0;
                  validTrapItems[loc] = 0;
                }
                function addLocation(include: BitArray) {
                  const { regionIsHardLocked } = store._regionLocks();
                  const visited = new BitArray(include.length);
                  const q = new Queue<number>();

                  function tryEnqueue(loc: number) {
                    if (visited[loc]) {
                      return;
                    }

                    if (!regionIsHardLocked[locs[loc].regionLocationKey[0]]) {
                      q.enqueue(loc);
                    }

                    visited[loc] = 1;
                  }

                  tryEnqueue(prev.currentLocation);
                  for (let loc = q.dequeue(); loc !== undefined; loc = q.dequeue()) {
                    if (include[loc]) {
                      include[loc] = 0;
                      a.push(loc);
                      break;
                    }

                    for (const [c] of locs[loc].connected.all) {
                      tryEnqueue(c);
                    }
                  }
                }

                for (const item of items) {
                  const itemFull = allItems[item];
                  r.push(item);
                  if (r.size <= result.processedReceivedItemCount) {
                    continue;
                  }

                  let subtractConfidence = false;
                  let addConfidence = false;
                  for (const aura of itemFull.aurasGranted) {
                    switch (aura) {
                      case 'well_fed':
                        result.foodFactor += 5;
                        break;

                      case 'upset_tummy':
                        if (result.hasConfidence) {
                          subtractConfidence = true;
                        }
                        else {
                          result.foodFactor -= 5;
                        }

                        break;

                      case 'lucky':
                        ++result.luckFactor;
                        break;

                      case 'unlucky':
                        if (result.hasConfidence) {
                          subtractConfidence = true;
                        }
                        else {
                          --result.luckFactor;
                        }

                        break;

                      case 'energized':
                        result.energyFactor += 5;
                        break;

                      case 'sluggish':
                        if (result.hasConfidence) {
                          subtractConfidence = true;
                        }
                        else {
                          result.energyFactor -= 5;
                        }

                        break;

                      case 'distracted':
                        if (result.hasConfidence) {
                          subtractConfidence = true;
                        }
                        else {
                          ++result.distractionCounter;
                        }

                        break;

                      case 'stylish':
                        result.styleFactor += 2;
                        break;

                      case 'startled':
                        if (result.hasConfidence) {
                          subtractConfidence = true;
                        }
                        else {
                          ++result.startledCounter;
                        }

                        break;

                      case 'smart':
                        addLocation(validProgressionItems);
                        break;

                      case 'conspiratorial':
                        if (result.hasConfidence) {
                          subtractConfidence = true;
                        }
                        else {
                          addLocation(validTrapItems);
                        }
                        break;

                      case 'confident':
                        addConfidence = true;
                        break;
                    }
                  }

                  if (subtractConfidence) {
                    result.hasConfidence = false;
                  }

                  if (addConfidence) {
                    result.hasConfidence = true;
                  }
                }

                // Startled is extremely punishing. after a big release, it can be very annoying to just
                // sit there and wait for too many turns in a row. same concept applies to Distracted.
                result.startledCounter = Math.min(result.startledCounter, 3);
                result.distractionCounter = Math.min(result.distractionCounter, 3);
              });
            });

            result.processedReceivedItemCount = result.receivedItems.size;
            return result;
          });
        },
        consumeOutgoingMoves() {
          const outgoingMoves = store.outgoingMoves();
          if (outgoingMoves.size > 0) {
            patchState(store, { outgoingMoves: outgoingMoves.clear() });
          }
          return outgoingMoves;
        },
        processMessage(msg: Message, players: PlayersManager) {
          switch (msg.type) {
            case 'playerChat':
              if (msg.player.team !== players.self.team) {
                return;
              }
              // eslint-disable-next-line no-fallthrough
            case 'serverChat':
              break;

            default:
              return;
          }

          const command = parseCommand(msg, players);
          if (command === null) {
            return;
          }

          const defs = store.defs();
          switch (command.type) {
            case 'help': {
              patchState(store, ({ outgoingMessages }) => ({
                outgoingMessages: outgoingMessages.push(
                  'Commands you can use are:',
                  `1. ${command.actualTag}go LOCATION_NAME`,
                  `2. ${command.actualTag}stop LOCATION_NAME`,
                  `3. ${command.actualTag}list`,
                  'LOCATION_NAME refers to whatever text you got in your hint, like "Basketball" or "Before Prawn Stars #12".',
                ),
              }));
              break;
            }

            case 'unrecognized': {
              patchState(store, ({ outgoingMessages }) => ({
                outgoingMessages: outgoingMessages.push(
                  `Say "${command.actualTag}help" (without the quotes) for a list of commands.`,
                ),
              }));
              break;
            }

            case 'go':
            case 'stop': {
              const { locationName } = command;
              const loc = defs.locationNameLookup.get(locationName) ?? NaN;
              let requestingSlotNumber = 0;
              let respondTo = '[Server]';
              if (msg.type === 'playerChat') {
                requestingSlotNumber = msg.player.slot;
                respondTo = msg.player.alias;
              }

              let outgoingMessage: string;
              if (Number.isNaN(loc)) {
                outgoingMessage = `Um... excuse me, but... I don't know what a '${locationName}' is...`;
              }
              else if (command.type === 'go') {
                const result = addUserRequestedLocation(requestingSlotNumber, loc);
                outgoingMessage = getMessageForAddUserRequestedLocationResult(loc, requestingSlotNumber, result, respondTo, players);
              }
              else {
                const result = removeUserRequestedLocation(requestingSlotNumber, loc);
                outgoingMessage = getMessageForRemoveUserRequestedLocationResult(loc, requestingSlotNumber, result, respondTo, players);
              }

              patchState(store, ({ outgoingMessages }) => ({ outgoingMessages: outgoingMessages.push(outgoingMessage) }));
              break;
            }

            case 'list': {
              const { regionIsHardLocked } = store._regionLocks();
              const userRequestedLocations = store.userRequestedLocations();
              if (userRequestedLocations.size === 0) {
                patchState(store, ({ outgoingMessages }) => ({
                  outgoingMessages: outgoingMessages.push(
                    'I don\'t have anything I\'m trying to get to... oh no, was I supposed to?',
                  ),
                }));
                return;
              }

              const userRequestedLocationsSet = new Set(userRequestedLocations.map(l => l.location));
              if (userRequestedLocationsSet.size === 1) {
                patchState(store, ({ outgoingMessages }) => ({
                  outgoingMessages: outgoingMessages.push(
                    `I'm focusing on trying to get to just 1 place: ${locationTag([...userRequestedLocationsSet][0])}`,
                  ),
                }));
                return;
              }

              const firstMessage = userRequestedLocationsSet.size > 5
                ? `I have a list of ${userRequestedLocationsSet.size.toString()} places that I'm going to focus on getting to. Here are the first 5:`
                : `Here are the ${userRequestedLocationsSet.size.toString()} places that I'm going to focus on getting to:`;
              patchState(store, ({ outgoingMessages }) => ({
                outgoingMessages: outgoingMessages.push(
                  firstMessage,
                  ...[...userRequestedLocationsSet].slice(0, 5).map(l => locationTag(l)),
                ),
              }));

              function locationTag(location: number) {
                const { allLocations } = defs;
                const loc = allLocations[location];
                return `'${loc.name}' (${regionIsHardLocked[loc.regionLocationKey[0]] ? 'blocked' : 'unblocked'})`;
              }
            }
          }
        },
        advance() {
          let remainingActions = 3;
          let multi = 0;
          let isFirstCheck = true;
          let bumpMercyModifierForNextTime = false;
          const { allLocations } = store.defs();
          patchState(store, (prev) => {
            const result = {} as Mutable<Partial<typeof prev>>;

            if (prev.sluggishCarryover) {
              remainingActions--;
              result.sluggishCarryover = false;
            }

            if (prev.foodFactor < 0) {
              remainingActions--;
              result.foodFactor = prev.foodFactor + 1;
            }
            else if (prev.foodFactor > 0) {
              remainingActions++;
              result.foodFactor = prev.foodFactor - 1;
            }

            if (prev.distractionCounter > 0) {
              // being startled takes priority over a distraction. you just saw a ghost, you're not
              // thinking about the Rubik's Cube that you got at about the same time!
              if (prev.startledCounter === 0) {
                remainingActions = 0;
              }

              result.distractionCounter = prev.distractionCounter - 1;
            }

            return result;
          });

          let confirmedTarget = false;

          // positive energyFactor lets the player make up to 2x as much distance in a single round of
          // (only) movement. in the past, this was uncapped, which basically meant that the player
          // would often teleport great distances, which was against the spirit of the whole thing.
          let energyBank = remainingActions;
          while (remainingActions > 0 && store.checkedLocations().size < allLocations.length) {
            patchState(store, (prev) => {
              const result = {} as Mutable<Partial<typeof prev>>;

              --remainingActions;
              if (!confirmedTarget) {
                const targetLocationEvidence = store.targetLocationEvidence();
                if (!targetLocationEvidenceEquals(prev.previousTargetLocationEvidence, targetLocationEvidence)) {
                  if (prev.startledCounter > 0) {
                    ++remainingActions;
                  }

                  result.previousTargetLocationEvidence = targetLocationEvidence;
                  confirmedTarget = true;
                  return result;
                }
              }

              let moved = false;
              result.currentLocation = prev.currentLocation;
              const targetLocation = store.targetLocation();
              if (result.currentLocation !== targetLocation) {
                if (prev.energyFactor < 0) {
                  --remainingActions;
                  result.energyFactor = prev.energyFactor + 1;
                }
                else if (prev.energyFactor > 0 && energyBank > 0) {
                  ++remainingActions;
                  --energyBank;
                  result.energyFactor = prev.energyFactor - 1;
                }

                // we're not in the right spot, so we're going to move at least a bit. playtesting
                // has shown that very long moves can be very boring (and a little too frequent). to
                // combat this, every time the player decides to move, they can advance up to three
                // whole spaces towards their target. this keeps the overall progression speed the
                // same in dense areas.
                const route = store.targetLocationRoute();
                let j = 0;
                while (route[j] !== result.currentLocation) {
                  j++;
                  if (j >= route.length) {
                    throw new Error('route did not contain the current location!');
                  }
                }
                for (let i = 0; i < 3 && result.currentLocation !== targetLocation; i++) {
                  if (!('outgoingMoves' in result)) {
                    result.outgoingMoves = prev.outgoingMoves;
                  }

                  result.outgoingMoves = result.outgoingMoves.push([result.currentLocation, result.currentLocation = route[++j]]);
                  moved = true;
                }
              }

              if (!moved && prev.startledCounter === 0 && !(store.locationIsChecked()[result.currentLocation])) {
                let unlucky = false;
                let lucky = false;
                let stylish = false;
                if (prev.luckFactor < 0) {
                  unlucky = true;
                  result.luckFactor = prev.luckFactor + 1;
                }
                else if (prev.luckFactor > 0) {
                  lucky = true;
                  result.luckFactor = prev.luckFactor - 1;
                }

                if (prev.styleFactor > 0 && !lucky) {
                  result.styleFactor = prev.styleFactor - 1;
                  stylish = true;
                }

                let roll = 0;
                let success = lucky;
                if (success) {
                  if (!IS_VITEST) {
                    console.log('lucky!');
                  }
                }
                if (!success) {
                  let d20: number;
                  [d20, result.prng] = rand.uniformIntDistribution(1, 20, prev.prng);
                  roll = modifyRoll({
                    d20,
                    ratCount: store.ratCount(),
                    mercy: prev.mercyFactor,
                    stylish,
                    unlucky,
                    multi: multi++,
                  });
                  success = roll >= allLocations[result.currentLocation].abilityCheckDC;
                  if (!IS_VITEST) {
                    console.log(roll, '=', d20, roll >= d20 ? '+' : '-', Math.abs(roll - d20), success ? '>=' : '<', allLocations[result.currentLocation].abilityCheckDC);
                  }
                  if (isFirstCheck && !success) {
                    bumpMercyModifierForNextTime = true;
                  }

                  isFirstCheck = false;
                }

                if (!success) {
                  return result;
                }
                if (!IS_VITEST) {
                  console.log('success at', allLocations[result.currentLocation].name);
                }
                result.outgoingCheckedLocations = prev.outgoingCheckedLocations.push(result.currentLocation);
                result.checkedLocations = prev.checkedLocations.add(result.currentLocation);
                result.mercyFactor = 0;
                bumpMercyModifierForNextTime = false;
              }

              if (result.currentLocation === targetLocation) {
                switch (store.targetLocationReason()) {
                  case 'user-requested':
                    result.userRequestedLocations = prev.userRequestedLocations.filter(l => l.location !== targetLocation);
                    break;

                  case 'aura-driven':
                    // this is sometimes redundant with the effect in onInit, but I think that it's
                    // also required here because microtasks aren't scheduled until after we return.
                    result.auraDrivenLocations = prev.auraDrivenLocations.filter(l => l !== targetLocation);
                    break;
                }
              }

              return result;
            });
          }

          patchState(store, (prev) => {
            const result = {} as Mutable<Partial<typeof prev>>;
            result.sluggishCarryover = remainingActions < 0;
            if (prev.startledCounter > 0) {
              result.startledCounter = prev.startledCounter - 1;
            }
            if (bumpMercyModifierForNextTime) {
              result.mercyFactor = prev.mercyFactor + 1;
            }

            return result;
          });
        },
      };
    }),
    withHooks({
      onInit(store) {
        effect(() => {
          const checkedLocations = store.checkedLocations();
          const auraDrivenLocations = store.auraDrivenLocations();
          const uncheckedAuraDrivenLocations = auraDrivenLocations.filter(l => !checkedLocations.has(l));
          if (uncheckedAuraDrivenLocations.size !== auraDrivenLocations.size) {
            patchState(store, { auraDrivenLocations: uncheckedAuraDrivenLocations });
          }
        });
      },
    }),
  );
}
