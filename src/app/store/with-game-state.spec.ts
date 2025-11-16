import { inject, InjectionToken, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import BitArray from '@bitarray/typedarray';
import { patchState, signalStore, withHooks } from '@ngrx/signals';
import rand from 'pure-rand';
import { describe, expect, test } from 'vitest';
import { BAKED_DEFINITIONS_BY_VICTORY_LANDMARK, type VictoryLocationYamlKey } from '../data/resolved-definitions';
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
  test('should initialize basic state', () => {
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
  });
});

function getStoreWith(initialData: Partial<DefiningGameState>): InstanceType<typeof TestingStore> {
  return TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      TestingStore,
      { provide: INITIAL_DATA, useValue: initialData },
    ],
  }).inject(TestingStore);
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
