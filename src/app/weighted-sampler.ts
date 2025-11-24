interface Weighted<T> {
  weight: number;
  item: T;
}

export function createWeightedSampler<T>(weightedItems: Iterable<Weighted<T>>): (this: void, roll: number) => T {
  const items: T[] = [];
  const weights: number[] = [];
  let sum = 0;
  for (const { weight, item } of weightedItems) {
    weights.push(weight);
    sum += weight;
    items.push(item);
  }
  let prev = 0;
  for (let i = 0; i < weights.length; i++) {
    const n = prev + weights[i];
    prev = n;
    weights[i] = n / sum;
  }

  return (n: number) => {
    let lo = 0;
    let hi = weights.length - 1;
    while (lo <= hi) {
      const mid = (hi + lo) >>> 1;
      if (weights[mid] <= n) {
        lo = mid + 1;
      }
      else {
        hi = mid - 1;
      }
    }
    return items[lo];
  };
}
