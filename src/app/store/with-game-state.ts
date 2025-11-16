import { computed, untracked } from '@angular/core';
import BitArray from '@bitarray/typedarray';
import { patchState, signalStoreFeature, withComputed, withMethods, withState } from '@ngrx/signals';
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

import {
  type TargetLocationEvidence,
  targetLocationEvidenceEquals,
  targetLocationEvidenceToJSONSerializable,
} from '../game/target-location-evidence';
import { type EnumVal, type Mutable } from '../util';

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
    && bitArraysEqual(a.regionIsLandmarkAndNotHardLocked, b.regionIsLandmarkAndNotHardLocked)
    && bitArraysEqual(a.regionIsLandmarkAndNotSoftLocked, b.regionIsLandmarkAndNotSoftLocked);
}

function findPlayerByAlias(players: PlayersManager, alias: string) {
  for (const t of players.teams) {
    for (const p of t) {
      if (p.alias === alias) {
        return p;
      }
    }
  }
  return null;
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
  previousTargetLocationEvidence: {
    isStartled: false,
    userRequestedLocations: null,
    firstAuraDrivenLocation: null,
    clearedOrClearableLandmarks: List<number>(),
  },
  receivedItems: List<number>(),
  checkedLocations: ImmutableSet<number>(),
  prng: rand.xoroshiro128plus(42),
  outgoingCheckedLocations: ImmutableSet<number>(),
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
      const _regionLocks = computed<RegionLocks>(() => {
        const { allRegions, startRegion } = defs();
        const locationIsChecked_ = locationIsChecked();
        const regionIsHardLocked = new BitArray(allRegions.length);
        const regionIsSoftLocked = new BitArray(allRegions.length);
        const regionIsLandmarkAndNotHardLocked = new BitArray(allRegions.length);
        const regionIsLandmarkAndNotSoftLocked = new BitArray(allRegions.length);
        for (let i = 0; i < allRegions.length; i++) {
          regionIsHardLocked[i] = 1;
          regionIsSoftLocked[i] = 1;
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
            if (!isSatisfied(region.requirement)) {
              continue;
            }
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
        workDone: store.workDone(),
        auraDrivenLocations: store.auraDrivenLocations().toJS(),
        userRequestedLocations: store.userRequestedLocations().toJS().map(l => ({
          location: l.location,
          userSlot: l.userSlot,
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
        targetLocationChosenBecauseSmart,
        targetLocationChosenBecauseConspiratorial,
        targetLocationRoute,
        asStoredData,
      };
    }),
    withMethods(store => ({
      consumeOutgoingMoves() {
        const outgoingMoves = store.outgoingMoves();
        if (outgoingMoves.size > 0) {
          patchState(store, { outgoingMoves: outgoingMoves.clear() });
        }
        return outgoingMoves;
      },
      processMessage(msg: Message, players: PlayersManager) {
        const text = msg.nodes.map(n => n.text).join('');
        let taggedSlotOrAlias = `@${players.self.name} `;
        let tagIndex = text.indexOf(taggedSlotOrAlias);
        if (tagIndex < 0) {
          taggedSlotOrAlias = `@${players.self.alias} `;
          tagIndex = text.indexOf(taggedSlotOrAlias);
        }

        if (tagIndex < 0) {
          return;
        }

        // chat message format is "{UserAlias}: {Message}", so it needs to be at least this long.
        if (tagIndex <= ': '.length) {
          return;
        }

        const probablyPlayerAlias = text.substring(0, tagIndex - ': '.length);
        const requestingPlayer = findPlayerByAlias(players, probablyPlayerAlias);
        const requestingSlotNumber = requestingPlayer?.slot ?? 0;
        if (probablyPlayerAlias !== '[Server]') {
          if (!requestingPlayer) {
            // this isn't necessarily an error or a mistaken assumption. it could just be that the
            // '@${SlotName}' happened partway through their message. don't test every single user's
            // alias against every single chat message that contains '@${SlotName}', just require it
            // to be at the start of the message. done.
            return;
          }

          if (requestingPlayer.team !== players.self.team) {
            // nice try
            const outgoingMessage = `${probablyPlayerAlias}, only members of my team can send me commands.`;
            patchState(store, ({ outgoingMessages }) => ({ outgoingMessages: outgoingMessages.push(outgoingMessage) }));
            return;
          }
        }

        // if we got here, then the entire rest of the message after "@{SlotName}" is the command.
        const defs = store.defs();
        const slotOrAliasMatcher = new RegExp(`^${RegExp.escape(taggedSlotOrAlias.normalize())}`);
        const cmd = text.substring(tagIndex).normalize().replace(slotOrAliasMatcher, '');
        if (/^go /i.exec(cmd)) {
          const quotesMatcher = /^"*|"*$/g;
          const locName = cmd.substring('go '.length).replaceAll(quotesMatcher, '');
          const loc = defs.locationNameLookup.get(locName);
          if (loc) {
            // get the true name of it
            const exactLocName = defs.allLocations[loc].name;
            if (store.auraDrivenLocations().includes(loc)) {
              const outgoingMessage = store.locationIsProgression()[loc]
                ? `Don't worry, ${probablyPlayerAlias}, a little bird already told me ALL about ${exactLocName}.`
                : `Don't worry, ${probablyPlayerAlias}, I'm already on my way to ${exactLocName} after getting an anonymous tip about it.`;
              patchState(store, ({ outgoingMessages }) => ({ outgoingMessages: outgoingMessages.push(outgoingMessage) }));
              return;
            }

            const alreadyRequested = store.userRequestedLocations().find(l => l.location === loc);
            if (alreadyRequested) {
              if (alreadyRequested.userSlot === requestingSlotNumber) {
                const outgoingMessage = `Hey, ${probablyPlayerAlias}, no worries, I remember ${exactLocName} from when you asked me before.`;
                patchState(store, ({ outgoingMessages }) => ({ outgoingMessages: outgoingMessages.push(outgoingMessage) }));
              }
              else {
                const firstRequestingUser = alreadyRequested.userSlot === 0
                  ? 'the server'
                  : players.teams[players.self.team][alreadyRequested.userSlot].alias;
                const outgoingMessage = `All right, I'll prioritize ${exactLocName} for you, ${probablyPlayerAlias}. Just so you know, ${firstRequestingUser} already asked me to go there first, but I'll remember that you want me to go there too, in case they change their mind.`;
                patchState(store, ({ userRequestedLocations, outgoingMessages }) => ({
                  userRequestedLocations: userRequestedLocations.push({ location: loc, userSlot: requestingSlotNumber }),
                  outgoingMessages: outgoingMessages.push(outgoingMessage),
                }));
              }
            }
            else {
              const outgoingMessage = store._regionLocks().regionIsHardLocked[defs.allLocations[loc].regionLocationKey[0]]
                ? `I'll keep it in mind that ${exactLocName} is important to you, ${probablyPlayerAlias}. I can't get there just yet, though, so please be patient with me...`
                : `All right, I'll prioritize ${exactLocName} for you, ${probablyPlayerAlias}!`;
              patchState(store, ({ userRequestedLocations, outgoingMessages }) => ({
                userRequestedLocations: userRequestedLocations.push({ location: loc, userSlot: requestingSlotNumber }),
                outgoingMessages: outgoingMessages.push(outgoingMessage),
              }));
            }
          }
          else {
            const outgoingMessage = `Um... excuse me, but... I don't know what a '${locName}' is...`;
            patchState(store, ({ outgoingMessages }) => ({ outgoingMessages: outgoingMessages.push(outgoingMessage) }));
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
          const result = { } as Mutable<Partial<typeof prev>>;

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

        // these two will be needed for the TODO below.
        /*
        let locationAttempts = 0;
        let checkedAnyLocations = false;
        */
        let confirmedTarget = false;

        // positive energyFactor lets the player make up to 2x as much distance in a single round of
        // (only) movement. in the past, this was uncapped, which basically meant that the player
        // would often teleport great distances, which was against the spirit of the whole thing.
        let energyBank = remainingActions;
        while (remainingActions > 0 && store.checkedLocations().size < allLocations.length) {
          patchState(store, (prev) => {
            const result = { } as Mutable<Partial<typeof prev>>;

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
                console.log('lucky!');
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
                console.log(roll, '=', d20, roll >= d20 ? '+' : '-', Math.abs(roll - d20), success ? '>=' : '<', allLocations[result.currentLocation].abilityCheckDC);
                if (isFirstCheck && !success) {
                  bumpMercyModifierForNextTime = true;
                }

                isFirstCheck = false;
              }

              if (success) {
                console.log('success at', allLocations[result.currentLocation].name);
                result.outgoingCheckedLocations = prev.outgoingCheckedLocations.add(result.currentLocation);
                result.checkedLocations = prev.checkedLocations.add(result.currentLocation);
                result.mercyFactor = 0;
                bumpMercyModifierForNextTime = false;
              }
            }

            if (result.currentLocation === targetLocation) {
              switch (store.targetLocationReason()) {
                case 'user-requested':
                  result.userRequestedLocations = prev.userRequestedLocations.filter(l => l.location !== targetLocation);
                  break;

                case 'aura-driven':
                  result.auraDrivenLocations = prev.auraDrivenLocations.filter(l => l !== targetLocation);
                  break;
              }
            }

            return result;
          });
        }

        patchState(store, (prev) => {
          const result = { } as Mutable<Partial<typeof prev>>;
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
    })),
  );
}
