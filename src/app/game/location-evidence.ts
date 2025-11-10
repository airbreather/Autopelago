import { List } from 'immutable';
import type { ToJSONSerializable } from '../util';
import type { UserRequestedLocation } from './defining-state';

interface LocationChosenBecauseStartled {
  startled: true;
}

interface LocationChosenBecauseAuraDriven {
  startled: false;
  firstAuraDrivenLocation: number;
}

interface LocationChosenBecauseUserRequested {
  startled: false;
  firstAuraDrivenLocation: null;
  clearedOrClearableLandmarks: List<number>;
  userRequestedLocations: List<Readonly<UserRequestedLocation>>;
}

interface LocationChosenBecauseClosestReachableUnchecked {
  startled: false;
  firstAuraDrivenLocation: null;
  clearedOrClearableLandmarks: List<number>;
  userRequestedLocations: null;
}

export type LocationEvidence =
  | null
  | LocationChosenBecauseStartled
  | LocationChosenBecauseAuraDriven
  | LocationChosenBecauseUserRequested
  | LocationChosenBecauseClosestReachableUnchecked
  ;

export function locationEvidenceToJSONSerializable(locationEvidence: LocationEvidence) {
  if (
    locationEvidence === null
    || locationEvidence.startled
    || locationEvidence.firstAuraDrivenLocation !== null) {
    return null;
  }

  return {
    ...locationEvidence,
    clearedOrClearableLandmarks: locationEvidence.clearedOrClearableLandmarks.toJS(),
    userRequestedLocations: locationEvidence.userRequestedLocations?.toJS() ?? null,
  };
}

export function locationEvidenceFromJSONSerializable(locationEvidence: ToJSONSerializable<LocationEvidence>) {
  if (
    locationEvidence === null
    || locationEvidence.startled
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

export function locationEvidenceEquals(a: LocationEvidence, b: LocationEvidence) {
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
