import BitArray from '@bitarray/typedarray';

import { AutopelagoDefinitions } from '../data/resolved-definitions';

interface DetermineTargetLocationOptionsCommon {
  currentLocation: number;
  isStartled: boolean;
  defs: Readonly<AutopelagoDefinitions>;
  landmarkRegionIsLocked: Readonly<BitArray>;
}

interface DetermineTargetLocationOptionsStartled extends DetermineTargetLocationOptionsCommon {
  isStartled: true;
  landmarkRegionIsChecked: Readonly<BitArray>;
}

interface DetermineTargetLocationOptionsNotStartled extends DetermineTargetLocationOptionsCommon {
  isStartled: false;
  isSmart: boolean;
  isConspiratorial: boolean;
}

export type DetermineTargetLocationOptions =
  DetermineTargetLocationOptionsNotStartled | DetermineTargetLocationOptionsStartled;

interface DetermineRouteOptionsCommon {
  currentLocation: number;
  targetLocation: number;
  isStartled: boolean;
  defs: Readonly<AutopelagoDefinitions>;
  landmarkRegionIsLocked: Readonly<BitArray>;
}

interface DetermineRouteOptionsStartled extends DetermineRouteOptionsCommon {
  isStartled: true;
  landmarkRegionIsChecked: Readonly<BitArray>;
}

export type DetermineRouteOptions =
  DetermineRouteOptionsCommon | DetermineRouteOptionsStartled;

export type TargetLocationReason =
  'game-not-started'
  | 'nowhere-useful-to-move'
  | 'closest-reachable-unchecked'
  | 'priority'
  | 'smart'
  | 'conspiratorial'
  | 'go-mode'
  | 'startled';

export interface TargetLocationResult {
  location: number;
  reason: TargetLocationReason;
}

export function determineTargetLocation(options: Readonly<DetermineTargetLocationOptions>): TargetLocationResult {
  return {
    location: options.currentLocation,
    reason: 'nowhere-useful-to-move',
  };
}

export function determineRoute(options: Readonly<DetermineRouteOptions>): number[] {
  return [...new Set([options.currentLocation, options.targetLocation])];
}
