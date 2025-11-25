import { describe, expect, test } from 'vitest';
import { createWeightedSampler } from './weighted-sampler';

function valsForWeights(weights: readonly number[]) {
  return weights.map((w, i) => ({ weight: w, item: i }));
}

function getSamples(vals: ReturnType<typeof valsForWeights>, rolls: readonly number[]) {
  const sampler = createWeightedSampler(vals);
  expect(sampler).not.toBeNull();
  const result = Array<number>(rolls.length);
  for (const [i, roll] of rolls.entries()) {
    result[i] = sampler?.(roll) ?? NaN;
  }
  return result.sort();
}

describe('weighted sampler', () => {
  test('4 equal weights, never exactly roll equal', () => {
    const vals = valsForWeights([1, 1, 1, 1]);
    const samples = getSamples(vals, [0.2, 0.4, 0.6, 0.8]);
    expect(samples).toStrictEqual([0, 1, 2, 3]);
  });
  test('4 equal weights, roll exact equal', () => {
    const vals = valsForWeights([1, 1, 1, 1]);
    const samples = getSamples(vals, [0, 0.25, 0.5, 0.75]);
    expect(samples).toStrictEqual([0, 1, 2, 3]);
  });
  test('6 not-all-equal weights, roll exact equal', () => {
    const vals = valsForWeights([1, 1, 2, 4, 8, 16]);
    // technically a bit white-box, but it's fine: there's absolutely no reason to imagine that an
    // implementation would do anything other than map one range per item.
    const samples = getSamples(vals, [0, 1, 2, 4, 8, 16].map(num => num / 32));
    expect(samples).toStrictEqual([0, 1, 2, 3, 4, 5]);
  });
  test('6 not-all-equal weights, never exactly roll equal', () => {
    const vals = valsForWeights([1, 1, 2, 4, 8, 16]);
    const samples = getSamples(vals, [0, 1, 2, 4, 8, 16].map(num => (num + 0.001) / 32));
    expect(samples).toStrictEqual([0, 1, 2, 3, 4, 5]);
  });
});
