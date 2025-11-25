export function binarySearch(arr: readonly number[], n: number) {
  let lo = 0;
  let hi = arr.length - 1;
  while (lo <= hi) {
    const mid = (hi + lo) >>> 1;
    if (arr[mid] <= n) {
      lo = mid + 1;
    }
    else {
      hi = mid - 1;
    }
  }
  return lo;
}
