import { List } from 'immutable';
import type { UserRequestedLocation } from '../data/slot-data';
import type { ToJSONSerializable } from '../utils/types';

interface Startled {
  isStartled: true;
}

interface AuraDriven {
  isStartled: false;
  firstAuraDrivenLocation: number;
}

interface UserRequested {
  isStartled: false;
  firstAuraDrivenLocation: null;
  clearedOrClearableLandmarks: List<number>;
  userRequestedLocations: List<UserRequestedLocation>;
}

interface ClosestReachableUncheckedOrNowhereToMove {
  isStartled: false;
  firstAuraDrivenLocation: null;
  clearedOrClearableLandmarks: List<number>;
  userRequestedLocations: null;
}

export type TargetLocationEvidence =
  | null
  | Startled
  | AuraDriven
  | UserRequested
  | ClosestReachableUncheckedOrNowhereToMove
  ;

export function targetLocationEvidenceToJSONSerializable(locationEvidence: TargetLocationEvidence): ToJSONSerializable<TargetLocationEvidence> {
  if (locationEvidence === null) {
    return null;
  }

  if (locationEvidence.isStartled) {
    return { isStartled: true };
  }

  if (typeof locationEvidence.firstAuraDrivenLocation === 'number') {
    return {
      isStartled: false,
      firstAuraDrivenLocation: locationEvidence.firstAuraDrivenLocation,
    };
  }

  return {
    isStartled: false,
    firstAuraDrivenLocation: null,
    clearedOrClearableLandmarks: locationEvidence.clearedOrClearableLandmarks.toJS(),
    userRequestedLocations: locationEvidence.userRequestedLocations
      ? [...locationEvidence.userRequestedLocations]
      : null,
  } satisfies ToJSONSerializable<TargetLocationEvidence>;
}

export function targetLocationEvidenceFromJSONSerializable(locationEvidence: ToJSONSerializable<TargetLocationEvidence>) {
  if (
    locationEvidence === null
    || locationEvidence.isStartled
    || locationEvidence.firstAuraDrivenLocation !== null) {
    return null;
  }

  return {
    ...locationEvidence,
    clearedOrClearableLandmarks: List(locationEvidence.clearedOrClearableLandmarks),
    userRequestedLocations: locationEvidence.userRequestedLocations === null
      ? null
      : List(locationEvidence.userRequestedLocations),
  };
}

export function targetLocationEvidenceEquals(a: TargetLocationEvidence, b: TargetLocationEvidence) {
  if (a === null) {
    return b === null;
  }

  if (b === null) {
    return false;
  }

  if (a.isStartled) {
    return b.isStartled;
  }

  if (b.isStartled) {
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
