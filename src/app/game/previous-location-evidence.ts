import { List } from 'immutable';
import type { UserRequestedLocation } from './state';

interface PreviousLocationChosenBecauseStartled {
  startled: true;
}

interface PreviousLocationChosenBecauseAuraDriven {
  startled: false;
  firstAuraDrivenLocation: number;
}

interface PreviousLocationChosenBecauseUserRequested {
  startled: false;
  firstAuraDrivenLocation: null;
  clearedOrClearableLandmarks: List<number>;
  userRequestedLocations: List<Readonly<UserRequestedLocation>>;
}

interface PreviousLocationChosenBecauseClosestReachableUnchecked {
  startled: false;
  firstAuraDrivenLocation: null;
  clearedOrClearableLandmarks: List<number>;
  userRequestedLocations: null;
}

export type PreviousLocationEvidence =
  | PreviousLocationChosenBecauseStartled
  | PreviousLocationChosenBecauseAuraDriven
  | PreviousLocationChosenBecauseUserRequested
  | PreviousLocationChosenBecauseClosestReachableUnchecked
  ;

export function previousLocationEvidenceEquals(a: PreviousLocationEvidence, b: PreviousLocationEvidence): boolean {
  if (a.startled) {
    return b.startled;
  }

  if (b.startled) {
    return false;
  }

  if (a.firstAuraDrivenLocation !== null) {
    return a.firstAuraDrivenLocation === b.firstAuraDrivenLocation;
  }

  if (b.firstAuraDrivenLocation !== null) {
    return false;
  }

  if (!a.clearedOrClearableLandmarks.equals(b.clearedOrClearableLandmarks)) {
    return false;
  }

  if (a.userRequestedLocations === null) {
    return b.userRequestedLocations === null;
  }

  return a.userRequestedLocations.equals(b.userRequestedLocations);
}
