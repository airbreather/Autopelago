// noinspection DuplicatedCode

import { inject, InjectionToken, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import BitArray from '@bitarray/typedarray';
import { getState, patchState, signalStore, withHooks } from '@ngrx/signals';
import { unprotected } from '@ngrx/signals/testing';
import { List, Range, Set as ImmutableSet } from 'immutable';
import rand from 'pure-rand';
import { describe, expect, test } from 'vitest';
import {
  type AutopelagoAura,
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  getLocs,
  type VictoryLocationYamlKey,
} from '../data/resolved-definitions';
import type { DefiningGameState } from '../game/defining-state';
import { type Mutable, stricterObjectFromEntries, strictObjectEntries } from '../util';
import { withGameState } from './with-game-state';

const singleAuraItems = stricterObjectFromEntries(
  (['well_fed', 'upset_tummy', 'lucky', 'unlucky', 'energized', 'sluggish', 'distracted', 'stylish', 'startled', 'smart', 'conspiratorial', 'confident'] as const)
    .map(aura => ([aura, BAKED_DEFINITIONS_FULL.allItems.findIndex(i => i.aurasGranted.length === 1 && i.aurasGranted[0] === aura)])),
) satisfies Record<AutopelagoAura, number>;

describe('self', () => {
  test.each(strictObjectEntries(prngs))('rolls for %s continue to match what they used to', (_name, { rolls, prng }) => {
    prng = prng.clone();
    for (const roll of rolls) {
      expect(rand.unsafeUniformIntDistribution(1, 20, prng)).toStrictEqual(roll);
    }
  });
});

const INITIAL_DATA = new InjectionToken<Partial<DefiningGameState>>('INITIAL_DATA');
const TestingStore = signalStore(
  withGameState(),
  withHooks({
    onInit(store) {
      patchState(store, inject(INITIAL_DATA));
    },
  }),
);

describe('withGameState', () => {
  test('first attempts should make sense', () => {
    const { startLocation, allLocations } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.snakes_on_a_planet;
    const store = getStoreWith({
      ...initialGameStateFor('snakes_on_a_planet'),
      prng: prngs._8_13_18_9_13.prng,
    });

    // we're on the first location. we should fail three times and then yield.
    const expectedPrng = prngs._8_13_18_9_13.prng.clone();
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);

    store.advance();

    expect(store.checkedLocations()).toStrictEqual(ImmutableSet());
    expect(store.mercyFactor()).toStrictEqual(1);
    expect(store.prng().getState()).toStrictEqual(expectedPrng.getState());

    // the next attempt should succeed despite rolling the same as the previous step because the
    // cumulative penalty has worn off and the mercy modifier adds +1.
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);

    store.advance();

    const secondLocation = allLocations[startLocation].connected.forward[0];
    expect([...store.checkedLocations()]).toStrictEqual([startLocation]);
    expect(store.targetLocation()).toStrictEqual(secondLocation);

    // because they succeeded on their first attempt, they have just enough actions to reach and
    // then make a feeble attempt at the next location on the route
    expect(store.currentLocation()).toStrictEqual(secondLocation);
    expect([...store.checkedLocations()]).toStrictEqual([startLocation]);
    expect(store.prng().getState()).toStrictEqual(expectedPrng.getState());

    // they made a successful check this round, so mercy factor shouldn't have been incremented!
    expect(store.mercyFactor()).toStrictEqual(0);
  });

  test.for([0, 1, 2, 3, 4, 5, 6])('should only try basketball with at least five rats: %d', (ratCount) => {
    const {
      allRegions,
      allLocations,
      itemNameLookup,
      locationNameLookup,
    } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.snakes_on_a_planet;
    const packRat = itemNameLookup.get('Pack Rat') ?? NaN;
    const basketball = locationNameLookup.get('Basketball') ?? NaN;
    const immediatelyBeforeBasketball = allLocations[basketball].connected.backward[0];
    const beforeBasketball = allRegions[allLocations[basketball].regionLocationKey[0]].connected.backward[0];

    const store = getStoreWith({
      ...initialGameStateFor('snakes_on_a_planet'),
      checkedLocations: ImmutableSet(getLocs(allRegions[beforeBasketball])),
      receivedItems: List(Array<number>(ratCount).fill(packRat)),
      currentLocation: immediatelyBeforeBasketball,
      prng: prngs.lucky.prng,
    });
    store.advance();
    expect(store.checkedLocations().has(basketball)).toEqual(ratCount >= 5);
  });
  // note: very early versions of the game used to go towards whichever of the two landmarks was
  // unblocked, but that turned out to be overkill. the separate tests remained, but their asserts
  // were changed to always just accept either direction.
  test.for(['Angry Turtles', 'Prawn Stars'])('should head further after completing Basketball (unblock %s first)', (unblockFirst) => {
    const {
      allRegions,
      allLocations,
      itemNameLookup,
      locationNameLookup,
    } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.snakes_on_a_planet;
    const packRat = itemNameLookup.get('Pack Rat') ?? NaN;
    const pizzaRat = itemNameLookup.get('Pizza Rat') ?? NaN;
    const premiumCanOfPrawnFood = itemNameLookup.get('Premium Can of Prawn Food') ?? NaN;
    const basketball = locationNameLookup.get('Basketball') ?? NaN;
    const prawnStars = locationNameLookup.get('Prawn Stars') ?? NaN;
    const angryTurtles = locationNameLookup.get('Angry Turtles') ?? NaN;
    const beforeBasketball = allRegions[allLocations[basketball].regionLocationKey[0]].connected.backward[0];
    const beforePrawnStars = allRegions[allLocations[prawnStars].regionLocationKey[0]].connected.backward[0];
    const beforeAngryTurtles = allRegions[allLocations[angryTurtles].regionLocationKey[0]].connected.backward[0];

    const store = getStoreWith({
      ...initialGameStateFor('snakes_on_a_planet'),
      checkedLocations: ImmutableSet(getLocs(allRegions[beforeBasketball])),
      receivedItems: List(Array<number>(5).fill(packRat)).push(unblockFirst === 'Angry Turtles' ? pizzaRat : premiumCanOfPrawnFood),
      currentLocation: basketball,
      prng: prngs.lucky.prng,
    });
    store.advance();

    // because we roll so well, we can actually use our three actions to complete two checks:
    // basketball, then move, then complete that first location that we moved to.
    expect(allLocations[store.currentLocation()].regionLocationKey).toBeOneOf([[beforePrawnStars, 0], [beforeAngryTurtles, 0]]);
    expect(allLocations[store.targetLocation()].regionLocationKey).toBeOneOf([[beforePrawnStars, 1], [beforeAngryTurtles, 1]]);
  });
  test.for(['captured_goldfish', 'secret_cache', 'snakes_on_a_planet'] as const)('game should be winnable (%s)', (victoryLocationYamlKey) => {
    const { allLocations, progressionItemsByYamlKey } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
    const store = getStoreWith(initialGameStateFor(victoryLocationYamlKey));
    let advancesSoFar = 0;
    const newReceivedItems: number[] = [];
    for (;;) {
      // always roll high. these are the slowest tests by far, and with all the other tests we have
      // for specific ranges and mercy factor, there's really no benefit to being "realistic" here.
      patchState(unprotected(store), { prng: prngs.lucky.prng });
      store.advance();

      // experimentally, this never exceeds 600 with these seeds.
      expect(++advancesSoFar).toBeLessThan(2000);

      const outgoingCheckedLocations = store.outgoingCheckedLocations();
      if (outgoingCheckedLocations.size === 0) {
        continue;
      }

      const currCheckedLocations = store.checkedLocations();
      if (currCheckedLocations.size === allLocations.length) {
        break;
      }

      for (const newCheckedLocation of outgoingCheckedLocations) {
        const location = allLocations[newCheckedLocation];
        if (location.unrandomizedProgressionItemYamlKey !== null) {
          newReceivedItems.push(progressionItemsByYamlKey.get(location.unrandomizedProgressionItemYamlKey) ?? NaN);
        }
      }

      if (newReceivedItems.length > 0) {
        store.receiveItems(newReceivedItems);
        newReceivedItems.length = 0;
      }

      patchState(unprotected(store), { outgoingCheckedLocations: outgoingCheckedLocations.clear() });
    }
  });

  test.for([1, 2, 3])('lucky aura should force success: %d instances', (effectCount) => {
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
    });
    store.receiveItems(Range(0, effectCount).map(() => singleAuraItems.lucky));
    for (let i = 0; i < 3; i++) {
      // lucky should force it, so all rolls should be low
      patchState(unprotected(store), { prng: prngs.unlucky.prng });
      store.advance();
    }

    expect(store.checkedLocations().size).toStrictEqual(effectCount);
  });

  test('unlucky aura should reduce modifier', () => {
    const { allRegions, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      prng: prngs._13_18_20_12_13.prng,
    });
    store.receiveItems(Range(0, 4).map(() => singleAuraItems.unlucky));

    // normally, a 13 as your first roll should pass, but with Unlucky it's not enough. the 18
    // also fails because -5 from the aura and -5 from the second attempt. even a natural 20
    // can't save you from a -15, so this first Advance call should utterly fail.
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet());

    // remember, after the first roll fails on a turn and no subsequent rolls pass during
    // that same turn, then the next turn's rolls get +1.
    expect(store.mercyFactor()).toStrictEqual(1);

    // the 12+1 burns the final Unlucky buff, so following it up with 13+1 overcomes the mere -5
    // from trying a second time on the same Advance call.
    store.advance();

    expect(store.checkedLocations()).toStrictEqual(ImmutableSet([getLocs(allRegions[startRegion])[0]]));

    // our first roll failed, but then a roll passed, so this modifier should be reset.
    expect(store.mercyFactor()).toStrictEqual(0);
  });

  test('positive energy factor should give extra movement', () => {
    const { itemNameLookup, locationNameLookup } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const packRat = itemNameLookup.get('Pack Rat') ?? NaN;
    const premiumCanOfPrawnFood = itemNameLookup.get('Premium Can of Prawn Food') ?? NaN;
    const pieRat = itemNameLookup.get('Pie Rat') ?? NaN;

    const basketball = locationNameLookup.get('Basketball') ?? NaN;
    const prawnStars = locationNameLookup.get('Prawn Stars') ?? NaN;
    const pirateBakeSale = locationNameLookup.get('Pirate Bake Sale') ?? NaN;

    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      checkedLocations: ImmutableSet([basketball, prawnStars]),
      userRequestedLocations: List([{ userSlot: 1, location: pirateBakeSale }]),
    });
    store.receiveItems([
      singleAuraItems.energized,
      ...Range(0, 5).map(() => packRat),
      premiumCanOfPrawnFood,
      pieRat,
    ]);

    for (let i = 0; i < 5; i++) {
      patchState(unprotected(store), { prng: prngs.unlucky.prng });
      store.advance();
    }

    expect(store.currentLocation()).toStrictEqual(pirateBakeSale);
  });

  test('negative energy factor should encumber movement', () => {
    const { allRegions, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const startRegionLocs = getLocs(allRegions[startRegion]);
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      energyFactor: -3,
      prng: prngs._20_20_1_20_20_20_20_1.prng,
    });

    // 3 actions are "check, move, (movement penalty)".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 1)));

    // 3 actions are "check, move, (movement penalty)" again.
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 2)));

    // 3 actions are "fail, check, move".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 3)));

    // 3 actions are "(movement penalty), check, move".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 4)));

    // 3 actions are "check, move, check".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 6)));
  });

  test('positive food factor should grant one extra action', () => {
    const { allLocations, allRegions, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const startRegionLocs = getLocs(allRegions[startRegion]);
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      foodFactor: 2,
      prng: prngs.lucky.prng,
    });

    // 4 actions are "check, move, check, move".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 2)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(2);

    // 4 actions are "check, move, check, move".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 4)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(4);

    // 3 actions are "check, move, check".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 6)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(5);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(6);

    store.receiveItems([singleAuraItems.well_fed]);

    // 4 actions are "move, check, move, check".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 8)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(7);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(8);
    expect(store.foodFactor()).toStrictEqual(4);
  });

  test('negative food factor should subtract one action', () => {
    const { allLocations, allRegions, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const startRegionLocs = getLocs(allRegions[startRegion]);
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      foodFactor: -2,
      prng: prngs.lucky.prng,
    });

    // 2 actions are "check, move".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 1)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(1);

    // 2 actions are "check, move".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 2)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(2);

    // 3 actions are "check, move, check".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 4)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(3);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(4);

    store.receiveItems([singleAuraItems.upset_tummy]);

    // 2 actions are "move, check".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 5)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(4);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(5);
    expect(store.foodFactor()).toStrictEqual(-4);
  });

  test('distraction counter should waste entire round', () => {
    const { allLocations, allRegions, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const startRegionLocs = getLocs(allRegions[startRegion]);
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      prng: prngs.lucky.prng,
    });

    // distraction should also burn through your food factor.
    store.receiveItems([singleAuraItems.well_fed]);

    // distraction counter won't go above 3.
    store.receiveItems(Range(0, 3).map(() => singleAuraItems.distracted));
    Range(0, 3).forEach(store.advance);
    store.receiveItems(Range(0, 2).map(() => singleAuraItems.distracted));
    Range(0, 2).forEach(store.advance);

    // 3 actions are "check, move, check"
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 2)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(1);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(2);
  });

  test('style factor should improve modifier', () => {
    const { allLocations, allRegions, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const startRegionLocs = getLocs(allRegions[startRegion]);
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      prng: prngs._6_11.prng,
    });

    store.receiveItems(Range(0, 2).map(() => singleAuraItems.stylish));

    // 3 actions are "check, move, check"
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet(startRegionLocs.slice(0, 2)));
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(1);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(2);
  });

  test('go mode', () => {
    const { allLocations, allRegions, progressionItemsByYamlKey, startRegion, victoryLocationsByYamlKey } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.snakes_on_a_planet;
    const victoryLocation = victoryLocationsByYamlKey.get('snakes_on_a_planet') ?? NaN;
    const lastItemYamlKey = 'mongoose_in_a_combat_spacecraft';
    const store = getStoreWith({
      ...initialGameStateFor('snakes_on_a_planet'),
      receivedItems: List(allLocations
        .filter(l => l.unrandomizedProgressionItemYamlKey !== null && l.unrandomizedProgressionItemYamlKey !== lastItemYamlKey)
        .map(l => progressionItemsByYamlKey.get(l.unrandomizedProgressionItemYamlKey ?? '') ?? NaN)),
      prng: prngs.unlucky.prng,
    });

    // make a couple of steps where we have all items except the very last one. this is SOMEWHAT
    // YAML-dependent, but seriously, if you advance 2 times with rolls forced to be natural 1,
    // and that somehow brings you out of the starting region, then that's a BIG change.
    for (let i = 0; i < 2; i++) {
      store.advance();
      expect(allLocations[store.targetLocation()].regionLocationKey[0]).toStrictEqual(startRegion);
      patchState(unprotected(store), { prng: prngs.unlucky.prng });
    }

    // now give it that last randomized item and see it shoot for the moon all the way through.
    store.receiveItems([progressionItemsByYamlKey.get(lastItemYamlKey) ?? NaN]);

    let advancesSoFar = 0;
    for (;;) {
      patchState(unprotected(store), { prng: prngs.lucky.prng });
      store.advance();
      if (store.locationIsChecked()[victoryLocation]) {
        break;
      }

      // it's probably impossible for it to even go above 31, probably.
      expect(++advancesSoFar).toBeLessThan(50);
      expect(allRegions[allLocations[store.targetLocation()].regionLocationKey[0]]).toHaveProperty('loc');
    }
  });

  test('user-requested locations should shift target', () => {
    const { itemNameLookup, locationNameLookup } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const packRat = itemNameLookup.get('Pack Rat') ?? NaN;
    const premiumCanOfPrawnFood = itemNameLookup.get('Premium Can of Prawn Food') ?? NaN;

    const basketball = locationNameLookup.get('Basketball') ?? NaN;
    const prawnStars = locationNameLookup.get('Prawn Stars') ?? NaN;

    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      prng: prngs.unlucky.prng,
    });

    // getState(store) isn't completely identical to initialGameStateFor: the former gets the values
    // for ALL the state, not just what we explicitly initialize in initialGameStateFor.
    const initialState = getState(store);
    expect(store.addUserRequestedLocation(1, prawnStars)).toStrictEqual({
      kind: 'newly-added',
    });
    // should NOT be targeting Prawn Stars now, because we can't reach it out the gate.
    expect(store.targetLocation()).not.toStrictEqual(prawnStars);

    // just restart it, giving it what's needed to reach Prawn Stars
    patchState(unprotected(store), {
      ...initialState,
      checkedLocations: ImmutableSet([basketball]),
      receivedItems: List([
        ...Range(0, 5).map(() => packRat),
        premiumCanOfPrawnFood,
      ]),
      prng: prngs.unlucky.prng,
    });
    const firstResult = store.addUserRequestedLocation(1, prawnStars);
    const sameUserResult = store.addUserRequestedLocation(1, prawnStars);
    expect(firstResult).toStrictEqual<typeof firstResult>({
      kind: 'newly-added',
    });
    expect(store.targetLocation()).toStrictEqual(prawnStars);
    expect(store.targetLocationReason()).toStrictEqual('user-requested');
    expect(sameUserResult).toStrictEqual<typeof sameUserResult>({
      kind: 'already-requested',
      userSlots: [1],
    });
    expect(store.userRequestedLocations()).toStrictEqual(List([
      { userSlot: 1, location: prawnStars },
    ]));
    const differentUserResult = store.addUserRequestedLocation(2, prawnStars);
    expect(differentUserResult).toStrictEqual<typeof differentUserResult>({
      kind: 'already-requested',
      userSlots: [1],
    });
    expect(store.userRequestedLocations()).toStrictEqual(List([
      { userSlot: 1, location: prawnStars },
      { userSlot: 2, location: prawnStars },
    ]));

    // teleport the rat over to Prawn Stars and have it do its thing (remember it's rolling all
    // natural 1s today).
    patchState(unprotected(store), { currentLocation: prawnStars });
    store.advance();

    // it should still be there, and it should still be our priority location.
    expect(store.currentLocation()).toStrictEqual(prawnStars);
    expect(store.userRequestedLocations()).toStrictEqual(List([
      { userSlot: 1, location: prawnStars },
      { userSlot: 2, location: prawnStars },
    ]));

    // now roll natural 20s.
    patchState(unprotected(store), { prng: prngs.lucky.prng });
    store.advance();

    expect(store.userRequestedLocations()).toStrictEqual(List());
  });

  test('startled should move player towards start', () => {
    const { allRegions, startLocation, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const lastLocationInStartRegion = getLocs(allRegions[startRegion])[-1];
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),

      // force the first steps to move it towards the last reachable location in this region
      prng: prngs.unlucky.prng,
      userRequestedLocations: List([{ userSlot: 0, location: lastLocationInStartRegion }]),
    });

    store.advance();
    const middleLocation = store.currentLocation();
    store.advance();
    // if this next one fails, then it's because the YAML file changed too much.
    expect(store.currentLocation()).not.toStrictEqual(lastLocationInStartRegion);

    // even though it's all high rolls, we shouldn't have any checks because the rat is hard-prioritizing.
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet());

    store.receiveItems([singleAuraItems.startled]);
    expect(store.targetLocation()).toStrictEqual(startLocation);

    // it used all its movement to get from middleLocation to here previously, so being startled
    // should cause it to use that same movement to get exactly back to middleLocation again.
    store.advance();
    expect(store.startledCounter()).toStrictEqual(0);
    expect(store.currentLocation()).toStrictEqual(middleLocation);
  });

  test('startled should take priority over distracted', () => {
    const { allRegions, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const startRegionLocs = getLocs(allRegions[startRegion]);
    const lastLocationInStartRegion = startRegionLocs.at(-1) ?? NaN;
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      prng: prngs.lucky.prng,
      currentLocation: lastLocationInStartRegion,
    });

    store.receiveItems([
      singleAuraItems.startled,
      ...Range(0, 2).map(() => singleAuraItems.distracted),
    ]);

    // first step, we're startled out of our distraction.
    store.advance();
    const expectedStartledTarget = startRegionLocs.at(-10) ?? NaN;
    expect(store.startledCounter()).toStrictEqual(0);
    expect(store.distractionCounter()).toStrictEqual(1);
    expect(store.currentLocation()).toStrictEqual(expectedStartledTarget);

    // second step, there's a new distraction that we hadn't gotten to yet.
    store.advance();
    // distraction burns a whole step
    expect(store.currentLocation()).toStrictEqual(expectedStartledTarget);

    // now we're fine. 3 actions are "reorient, check, move".
    store.advance();
    expect(store.checkedLocations()).toStrictEqual(ImmutableSet([expectedStartledTarget]));
    expect(store.currentLocation()).not.toStrictEqual(expectedStartledTarget);
  });

  test.each(['smart', 'conspiratorial'] as const)('%s should resolve to nearest reachable if possible (regression test for #100)', (aura) => {
    const { allLocations, allRegions, locationNameLookup, startLocation, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const locationsBeforePrawnStars = getLocs(allRegions[allRegions[allLocations[locationNameLookup.get('Prawn Stars') ?? NaN].regionLocationKey[0]].connected.backward[0]]);
    const startRegionLocs = getLocs(allRegions[startRegion]);
    const initialGameState = initialGameStateFor('captured_goldfish') as Required<DefiningGameState>;
    const spoilerData: Mutable<Readonly<BitArray>> = aura === 'conspiratorial'
      ? initialGameState.locationIsTrap
      : initialGameState.locationIsProgression;
    spoilerData[startLocation] = 1;
    spoilerData[locationsBeforePrawnStars[0]] = 1;
    spoilerData[locationsBeforePrawnStars.at(-1) ?? NaN] = 1;
    const store = getStoreWith({
      ...initialGameState,
      currentLocation: startRegionLocs.at(-1) ?? NaN,
    });

    // even though there's a target RIGHT on the other side, we still favor the nearest one that
    // we can already reach with what we currently have.
    store.receiveItems([singleAuraItems[aura]]);
    expect(store.auraDrivenLocations()).toStrictEqual(List([startLocation]));
    expect(store.targetLocation()).toStrictEqual(startLocation);
    expect(store.targetLocationReason()).toStrictEqual('aura-driven');

    // if there's nothing else that we can reach, then we should NOT target the unreachable one
    // that's just out of reach. it should just fizzle.
    store.receiveItems([singleAuraItems[aura]]);
    expect(store.auraDrivenLocations()).toStrictEqual(List([startLocation]));
    expect(store.targetLocation()).toStrictEqual(startLocation);
    expect(store.targetLocationReason()).toStrictEqual('aura-driven');

    // #100: it also shouldn't re-prioritize the same location after it's been checked. uniquely to
    // this version (and not the C# version that it's ported from), this will respond to the server
    // running a /send_location command. (we're probably still hopelessly lost for multiple active
    // sessions on the same slot, though).
    patchState(unprotected(store), ({ checkedLocations }) => ({ checkedLocations: checkedLocations.add(startLocation) }));
    TestBed.tick(); // white-box: requirement is satisfied via an effect in onInit
    expect(store.auraDrivenLocations()).toStrictEqual(List());
    store.receiveItems([singleAuraItems[aura]]);
    expect(store.auraDrivenLocations()).toStrictEqual(List());
  });

  test('user-requested locations past clearable landmarks should block the player (regression test for #45)', () => {
    const { allLocations, allRegions, itemNameLookup, locationNameLookup, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const packRat = itemNameLookup.get('Pack Rat') ?? NaN;
    const basketball = locationNameLookup.get('Basketball') ?? NaN;
    const locationsBeforePrawnStars = getLocs(allRegions[allRegions[allLocations[locationNameLookup.get('Prawn Stars') ?? NaN].regionLocationKey[0]].connected.backward[0]]);
    const startRegionLocs = getLocs(allRegions[startRegion]);

    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      receivedItems: List(Range(0, 5).map(() => packRat)),
      currentLocation: startRegionLocs.at(-1) ?? NaN,
      userRequestedLocations: List([{ userSlot: 0, location: locationsBeforePrawnStars[1] }]),
    });

    // roll as low as possible for a few steps (not enough for the mercy factor to guarantee it).
    const FAILURE_COUNT = 3;
    for (let i = 0; i < FAILURE_COUNT; i++) {
      patchState(unprotected(store), { prng: prngs.unlucky.prng });
      store.advance();
    }

    // no cheating -- it actually has to clear the landmark first!
    expect(store.currentLocation()).toStrictEqual(basketball);
    expect(store.targetLocationReason()).toStrictEqual('user-requested');

    // in fact, you know what, let's get a bit strict on exactly what needed to have happened here.
    // it really needed to TRY as much as possible at each step. the first one is less relevant to
    // what we're testing here since it depends on the details of target switching and movement, but
    // as long as FAILURE_COUNT is greater than 1, its last attempt should have spent all 3 actions
    // trying nothing but attempts at progressing towards its singular goal of helping the user out.
    expect(store.mercyFactor()).toStrictEqual(FAILURE_COUNT);

    // enforce it to the extent that its PRNG state must literally look exactly like it would look
    // after rolling 3 times from where we forced it to start. no learned helplessness or anything.
    const expectedPrng = prngs.unlucky.prng.clone();
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);
    expect(store.prng().getState()).toStrictEqual(expectedPrng.getState());
  });

  // this test was added in the C# version after playtesting revealed it to feel WAY too punishing
  // when you exhaust everything available down one path of a fork and then have to backtrack to go
  // the other way. we kept it so that it still costs two actions if you do a check between two move
  // actions (as opposed to having the second piggyback on the one before it and not cost anything),
  // but it felt just about right to get 3 spaces of movement from one action if you're ONLY moving.
  test('long moves should be accelerated', () => {
    const { allRegions, itemNameLookup, locationNameLookup, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const packRat = itemNameLookup.get('Pack Rat') ?? NaN;
    const basketball = locationNameLookup.get('Basketball') ?? NaN;
    const startRegionLocs = getLocs(allRegions[startRegion]);

    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      receivedItems: List(Range(0, 5).map(() => packRat)),
      userRequestedLocations: List([{ userSlot: 0, location: basketball }]),
    });

    store.advance();

    // it needs to have moved forward 9 times.
    expect(store.currentLocation()).toStrictEqual(startRegionLocs[9]);
  });

  test('user-requested location checks should bypass unreachable locations (regression test for #53)', () => {
    const { allRegions, locationNameLookup, startRegion } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const startRegionLocs = getLocs(allRegions[startRegion]);
    const lastLocationBeforeBasketball = startRegionLocs.at(-1) ?? NaN;
    const basketball = locationNameLookup.get('Basketball') ?? NaN;

    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      checkedLocations: ImmutableSet([lastLocationBeforeBasketball]),
      currentLocation: lastLocationBeforeBasketball,
      userRequestedLocations: List([
        { userSlot: 0, location: basketball },
        { userSlot: 1, location: lastLocationBeforeBasketball },
      ]),
      prng: prngs.unlucky.prng,
    });
    store.advance();

    expect(store.targetLocation()).not.toStrictEqual(lastLocationBeforeBasketball);
  });

  test('startled should not move through locked locations', () => {
    const { itemNameLookup, locationNameLookup, startLocation } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const packRat = itemNameLookup.get('Pack Rat') ?? NaN;
    const pricelessAntique = itemNameLookup.get('Priceless Antique') ?? NaN;
    const pieRat = itemNameLookup.get('Pie Rat') ?? NaN;
    const pizzaRat = itemNameLookup.get('Pizza Rat') ?? NaN;
    const chefRat = itemNameLookup.get('Chef Rat') ?? NaN;

    const basketball = locationNameLookup.get('Basketball') ?? NaN;
    const angryTurtles = locationNameLookup.get('Angry Turtles') ?? NaN;
    const prawnStars = locationNameLookup.get('Prawn Stars') ?? NaN;
    const restaurant = locationNameLookup.get('Restaurant') ?? NaN;
    const pirateBakeSale = locationNameLookup.get('Pirate Bake Sale') ?? NaN;
    const afterPirateBakeSale1 = locationNameLookup.get('After Pirate Bake Sale #1') ?? NaN;
    const bowlingBallDoor = locationNameLookup.get('Bowling Ball Door') ?? NaN;

    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      currentLocation: afterPirateBakeSale1,
      receivedItems: List([
        ...Range(0, 40).map(() => packRat),
        pricelessAntique,
        pieRat,
        pizzaRat,
        chefRat,
      ]),
      checkedLocations: ImmutableSet([
        basketball,
        angryTurtles,
        restaurant,
        bowlingBallDoor,
      ]),
    });
    for (let i = 0; i < 100; i++) {
      store.receiveItems([singleAuraItems.startled]);
      store.advance();
      if (store.currentLocation() === startLocation) {
        break;
      }
    }

    expect.soft(store.outgoingMoves()).not.toContain(pirateBakeSale);
    expect.soft(store.outgoingMoves()).not.toContain(prawnStars);
  });

  test('received items should apply auras', () => {
    const { itemNameLookup } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
    });

    store.receiveItems([
      // upset_tummy, upset_tummy, upset_tummy, unlucky, startled, startled, startled, sluggish
      'Rat Poison',

      // well_fed, energized, energized, energized
      'Bag of Powdered Sugar',

      // confidence
      'Weapons-grade Folding Chair',

      // stylish, distracted, sluggish
      'Itchy Iron Wool Sweater',

      // confidence
      'Weapons-grade Folding Chair',
    ].map(name => itemNameLookup.get(name) ?? NaN));

    expect.soft(store.foodFactor()).toStrictEqual(-10);
    expect.soft(store.luckFactor()).toStrictEqual(-1);
    expect.soft(store.startledCounter()).toStrictEqual(3);
    expect.soft(store.energyFactor()).toStrictEqual(10); // 5 canceled by the first confidence!
    expect.soft(store.styleFactor()).toStrictEqual(2);
    expect.soft(store.distractionCounter()).toStrictEqual(0); // canceled by the first confidence!
    expect.soft(store.hasConfidence()).toStrictEqual(true);
  });

  // TODO: this test actually seems to have caught a bug? not 100% sure, but I'm going to save this
  // as-is because I'm having a nontrivial time getting a debugger running.
  test.skip('regression test for #92, which was an issue with the "closest reachable unchecked" logic', () => {
    const { allLocations, allRegions, itemNameLookup, locationNameLookup } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.snakes_on_a_planet;
    const regionLocsByYamlKey = stricterObjectFromEntries(allRegions.map(r => [r.yamlKey, getLocs(r)]));
    const packRat = itemNameLookup.get('Pack Rat') ?? NaN;
    const giantNoveltyScissors = itemNameLookup.get('Giant Novelty Scissors') ?? NaN;
    const ninjaRat = itemNameLookup.get('Ninja Rat') ?? NaN;
    const chefRat = itemNameLookup.get('Chef Rat') ?? NaN;
    const computerRat = itemNameLookup.get('Computer Rat') ?? NaN;
    const notoriousRAT = itemNameLookup.get('Notorious R.A.T.') ?? NaN;

    const basketball = locationNameLookup.get('Basketball') ?? NaN;
    const angryTurtles = locationNameLookup.get('Angry Turtles') ?? NaN;
    const restaurant = locationNameLookup.get('Restaurant') ?? NaN;
    const bowlingBallDoor = locationNameLookup.get('Bowling Ball Door') ?? NaN;
    const capturedGoldfish = locationNameLookup.get('Captured Goldfish') ?? NaN;
    const beforeGoldfish2 = locationNameLookup.get('Before Captured Goldfish #2') ?? NaN;

    const store = getStoreWith({
      ...initialGameStateFor('snakes_on_a_planet'),
      currentLocation: beforeGoldfish2,
      receivedItems: List([
        giantNoveltyScissors,
        ninjaRat,
        chefRat,
        computerRat,
        notoriousRAT,
        ...Range(0, 14).map(() => packRat),
      ]),
      checkedLocations: ImmutableSet([
        basketball,
        angryTurtles,
        restaurant,
        bowlingBallDoor,
        capturedGoldfish,
        ...regionLocsByYamlKey.Menu, // "Before Basketball"
        ...regionLocsByYamlKey.before_prawn_stars,
        ...regionLocsByYamlKey.before_angry_turtles,
        ...regionLocsByYamlKey.after_restaurant,
        ...regionLocsByYamlKey.before_captured_goldfish,

        ...regionLocsByYamlKey.after_pirate_bake_sale.slice(3),
        ...regionLocsByYamlKey.before_computer_interface.slice(-3),
      ]),
      prng: prngs.lucky.prng,
    });

    // the rat has everything it needs to make a few location checks. make sure it does that and
    // doesn't instead go into a loop like it was seen doing before.
    const initialCheckedLocationCount = store.checkedLocations().size;
    const locationWasVisited = new BitArray(allLocations.length);
    let cnt = 0;
    for (;;) {
      store.advance();
      if (store.checkedLocations().size > initialCheckedLocationCount) {
        break;
      }

      // this part ensures that the test will not loop forever
      const currentLocation = store.currentLocation();
      expect(locationWasVisited[currentLocation]).toStrictEqual(0);
      locationWasVisited[currentLocation] = 1;
      ++cnt;
    }
    expect(cnt).toStrictEqual(-1);
  });
});

function getStoreWith(initialData: Partial<DefiningGameState>): InstanceType<typeof TestingStore> {
  const store = TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      TestingStore,
      { provide: INITIAL_DATA, useValue: initialData },
    ],
  }).inject(TestingStore);

  // tests are ported from the C# version which has a more primitive version of "do we need to burn
  // an action to figure out the new target location?": whereas it just checks to see if the target
  // location changed since the end of the previous turn, we actually simulate the rat spending an
  // action every time its state changed in a way that could theoretically affect the target - even
  // if the target didn't change! ...but I want the tests as close to the C# version as possible for
  // the initial port (at least), so by default let's set it up so that we DON'T burn actions.
  patchState(unprotected(store), { previousTargetLocationEvidence: store.targetLocationEvidence() });
  return store;
}

function initialGameStateFor(victoryLocationYamlKey: VictoryLocationYamlKey): Partial<DefiningGameState> {
  const defs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
  return {
    lactoseIntolerant: false,
    victoryLocationYamlKey,
    enabledBuffs: new Set(['confident', 'energized', 'lucky', 'smart', 'stylish', 'well_fed']),
    enabledTraps: new Set(['conspiratorial', 'distracted', 'sluggish', 'startled', 'unlucky', 'upset_tummy']),
    locationIsProgression: new BitArray(defs.allLocations.length),
    locationIsTrap: new BitArray(defs.allLocations.length),
    foodFactor: 0,
    luckFactor: 0,
    energyFactor: 0,
    styleFactor: 0,
    distractionCounter: 0,
    startledCounter: 0,
    hasConfidence: false,
    mercyFactor: 0,
    sluggishCarryover: false,
    processedReceivedItemCount: 0,
    currentLocation: defs.startLocation,
    workDone: 0,
  };
}

const prngs = {
  lucky: {
    rolls: Array<number>(8).fill(20),
    prng: rand.xoroshiro128plus.fromState([-1771948076, -285776121, 74720295, -1842210148]),
  },
  unlucky: {
    rolls: Array<number>(8).fill(1),
    prng: rand.xoroshiro128plus.fromState([110412471, 1130388068, -1591035982, 1997136400]),
  },
  _8_13_18_9_13: {
    rolls: [8, 13, 18, 9, 13],
    prng: rand.xoroshiro128plus.fromState([-1796786197, 968774573, 301784831, 2049717482]),
  },
  _13_18_20_12_13: {
    rolls: [13, 18, 20, 12, 13],
    prng: rand.xoroshiro128plus.fromState([1464423090, -986313637, 1654351542, -859231019]),
  },
  _20_20_1_20_20_20_20_1: {
    rolls: [20, 20, 1, 20, 20, 20, 20, 1],
    prng: rand.xoroshiro128plus.fromState([721066624, -315573484, -1770911155, -50848825]),
  },
  _6_11: {
    rolls: [6, 11],
    prng: rand.xoroshiro128plus.fromState([-1170094942, -569190855, 990179148, -182617468]),
  },
} as const;

// noinspection JSUnusedLocalSymbols
function _prngThatWillRoll(vals: readonly number[]): rand.RandomGenerator {
  const candidates = Array.from(vals).fill(0);
  let maxMatch = 0;
  let match = 0;
  let result = rand.xoroshiro128plus(1);
  let prng = result;
  while (match < candidates.length) {
    [candidates[match], prng] = rand.uniformIntDistribution(1, 20, prng);
    if (candidates[match] === vals[match]) {
      ++match;
      if (match > maxMatch) {
        console.log(result.getState(), '-->', vals.slice(0, match));
        maxMatch = match;
      }
    }
    else {
      result = prng;
      match = 0;
    }
  }

  return result;
}
