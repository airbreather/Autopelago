import BitArray from '@bitarray/typedarray';

export function arraysEqual(a: readonly number[], b: readonly number[]) {
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

export function bitArraysEqual(a: Readonly<BitArray>, b: Readonly<BitArray>) {
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
