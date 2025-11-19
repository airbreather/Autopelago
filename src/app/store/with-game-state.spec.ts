// noinspection DuplicatedCode

import { inject, InjectionToken, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import BitArray from '@bitarray/typedarray';
import { patchState, signalStore, withHooks } from '@ngrx/signals';
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
import { stricterObjectFromEntries, strictObjectEntries } from '../util';
import { withGameState } from './with-game-state';

const singleAuraItems = stricterObjectFromEntries(
  (['well_fed', 'upset_tummy', 'lucky', 'unlucky', 'energized', 'sluggish', 'distracted', 'stylish', 'startled', 'smart', 'conspiratorial', 'confident'] as const)
    .map(aura => ([aura, BAKED_DEFINITIONS_FULL.allItems.findIndex(i => i.aurasGranted.length === 1 && i.aurasGranted[0] === aura)]))
) satisfies Record<AutopelagoAura, number>;

describe('self', () => {
  test.each(strictObjectEntries(prngs))('rolls for %s continue to match what they used to', (name, { rolls, prng }) => {
    assertPrngWillRoll(rolls, prng);
  });
})

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
    let expectedPrng = prngs._8_13_18_9_13.prng.clone();
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);
    rand.unsafeUniformIntDistribution(1, 20, expectedPrng);

    store.advance();

    expect(store.checkedLocations().size).toStrictEqual(0);
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
      locationNameLookup
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
      locationNameLookup
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
    while (true) {
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

  test.for([1, 2, 3])('lucky aura should force success: %d instances', effectCount => {
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
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      prng: prngs._13_18_20_12_13.prng,
    });
    store.receiveItems(Range(0, 4).map(() => singleAuraItems.unlucky));

    // normally, a 13 as your first roll should pass, but with Unlucky it's not enough. the 18
    // also fails because -5 from the aura and -5 from the second attempt. even a natural 20
    // can't save you from a -15, so this first Advance call should utterly fail.
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(0);

    // remember, after the first roll fails on a turn and no subsequent rolls pass during
    // that same turn, then the next turn's rolls get +1.
    expect(store.mercyFactor()).toStrictEqual(1);

    // the 12+1 burns the final Unlucky buff, so following it up with 13+1 overcomes the mere -5
    // from trying a second time on the same Advance call.
    store.advance();

    expect(store.checkedLocations().size).toStrictEqual(1);

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
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      energyFactor: -3,
      prng: prngs._20_20_1_20_20_20_20_1.prng,
    });

    // 3 actions are "check, move, (movement penalty)".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(1);

    // 3 actions are "check, move, (movement penalty)" again.
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(2);

    // 3 actions are "fail, check, move".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(3);

    // 3 actions are "(movement penalty), check, move".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(4);

    // 3 actions are "check, move, check".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(6);
  });

  test('positive food factor should grant one extra action', () => {
    const { allLocations } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      foodFactor: 2,
      prng: prngs.lucky.prng,
    });

    // 4 actions are "check, move, check, move".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(2);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(2);

    // 4 actions are "check, move, check, move".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(4);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(4);

    // 3 actions are "check, move, check".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(6);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(5);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(6);

    store.receiveItems([singleAuraItems.well_fed]);

    // 4 actions are "move, check, move, check".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(8);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(7);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(8);
    expect(store.foodFactor()).toStrictEqual(4);
  });

  test('negative food factor should subtract one action', () => {
    const { allLocations } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      foodFactor: -2,
      prng: prngs.lucky.prng,
    });

    // 2 actions are "check, move".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(1);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(1);

    // 2 actions are "check, move".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(2);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(2);

    // 3 actions are "check, move, check".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(4);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(3);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(4);

    store.receiveItems([singleAuraItems.upset_tummy]);

    // 2 actions are "move, check".
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(5);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(4);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(5);
    expect(store.foodFactor()).toStrictEqual(-4);
  });

  test('distraction counter should waste entire round', () => {
    const { allLocations } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
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
    expect(store.checkedLocations().size).toStrictEqual(2);
    expect(allLocations[store.currentLocation()].regionLocationKey[1]).toStrictEqual(1);
    expect(allLocations[store.targetLocation()].regionLocationKey[1]).toStrictEqual(2);
  });

  test('style factor should improve modifier', () => {
    const { allLocations } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.captured_goldfish;
    const store = getStoreWith({
      ...initialGameStateFor('captured_goldfish'),
      prng: prngs._6_11.prng,
    });

    store.receiveItems(Range(0, 2).map(() => singleAuraItems.stylish));

    // 3 actions are "check, move, check"
    store.advance();
    expect(store.checkedLocations().size).toStrictEqual(2);
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
    while (true) {
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

function assertPrngWillRoll(vals: readonly number[], prng: rand.RandomGenerator) {
  let roll: number;
  for (let i = 0; i < vals.length; i++) {
    [roll, prng] = rand.uniformIntDistribution(1, 20, prng);
    expect(roll).toStrictEqual(vals[i]);
  }
}
