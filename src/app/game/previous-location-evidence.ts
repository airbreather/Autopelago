import { List } from 'immutable';
import type { ToJSONSerializable } from '../util';
import type { UserRequestedLocation } from './defining-state';

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
  | null
  | PreviousLocationChosenBecauseStartled
  | PreviousLocationChosenBecauseAuraDriven
  | PreviousLocationChosenBecauseUserRequested
  | PreviousLocationChosenBecauseClosestReachableUnchecked
  ;

export function previousLocationEvidenceToJSONSerializable(previousLocationEvidence: PreviousLocationEvidence): ToJSONSerializable<PreviousLocationEvidence> {
  if (
    previousLocationEvidence === null
    || previousLocationEvidence.startled
    || previousLocationEvidence.firstAuraDrivenLocation !== null) {
    return null;
  }

  return {
    ...previousLocationEvidence,
    clearedOrClearableLandmarks: previousLocationEvidence.clearedOrClearableLandmarks.toJS(),
    userRequestedLocations: previousLocationEvidence.userRequestedLocations?.toJS() ?? null,
  };
}

export function previousLocationEvidenceFromJSONSerializable(previousLocationEvidence: ToJSONSerializable<PreviousLocationEvidence>): PreviousLocationEvidence {
  if (
    previousLocationEvidence === null
    || previousLocationEvidence.startled
    || previousLocationEvidence.firstAuraDrivenLocation !== null) {
    return null;
  }

  return {
    ...previousLocationEvidence,
    clearedOrClearableLandmarks: List(previousLocationEvidence.clearedOrClearableLandmarks),
    userRequestedLocations: previousLocationEvidence.userRequestedLocations === null
      ? null
      : List(previousLocationEvidence.userRequestedLocations),
  };
}

export function previousLocationEvidenceEquals(a: PreviousLocationEvidence, b: PreviousLocationEvidence): boolean {
  if (a === null) {
    return b === null;
  }

  if (b === null) {
    return false;
  }

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
