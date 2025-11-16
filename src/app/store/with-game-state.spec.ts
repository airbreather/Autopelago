import { inject, InjectionToken, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import BitArray from '@bitarray/typedarray';
import { patchState, signalStore, withHooks } from '@ngrx/signals';
import { unprotected } from '@ngrx/signals/testing';
import { List, Set as ImmutableSet } from 'immutable';
import rand from 'pure-rand';
import { describe, expect, test } from 'vitest';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  getLocs,
  type VictoryLocationYamlKey,
} from '../data/resolved-definitions';
import type { DefiningGameState } from '../game/defining-state';
import { strictObjectEntries } from '../util';
import { withGameState } from './with-game-state';

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
    const { allRegions, allLocations, itemNameLookup, locationNameLookup } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.snakes_on_a_planet;
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
    const { allRegions, allLocations, itemNameLookup, locationNameLookup } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK.snakes_on_a_planet;
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
  // if the target didn't change! ...but I want the tests as close to the C# version as possible, so
  // by default let's set it up so that we DON'T burn an action each time. those tests that expect
  // the target to change will be able to replicate this by simply setting it to null.
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
