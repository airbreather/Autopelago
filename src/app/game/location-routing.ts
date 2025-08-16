import BitArray from '@bitarray/typedarray';

import { AutopelagoDefinitions } from '../data/resolved-definitions';

interface DetermineTargetLocationOptionsCommon {
  currentLocation: number;
  isStartled: boolean;
  defs: Readonly<AutopelagoDefinitions>;
  regionIsLocked: Readonly<BitArray>;
  locationIsChecked: Readonly<BitArray>;
}

interface DetermineTargetLocationOptionsStartled extends DetermineTargetLocationOptionsCommon {
  isStartled: true;
}

interface DetermineTargetLocationOptionsNotStartledCommon extends DetermineTargetLocationOptionsCommon {
  isStartled: false;
  victoryLandmarkRegion: number;
  locationIsAcceptedBySmart: Readonly<BitArray>;
  locationIsAcceptedByConspiratorial: Readonly<BitArray>;
}

interface DetermineTargetLocationOptionsNoExtraConditions extends DetermineTargetLocationOptionsNotStartledCommon {
  isSmart: false;
  isConspiratorial: false;
  userRequestedLocations: readonly number[];
}

interface DetermineTargetLocationOptionsNotStartledButSmart extends DetermineTargetLocationOptionsNotStartledCommon {
  isSmart: true;
  isConspiratorial: false;
}

interface DetermineTargetLocationOptionsNotStartledButConspiratorial extends DetermineTargetLocationOptionsNotStartledCommon {
  isSmart: false;
  isConspiratorial: true;
}

export type DetermineTargetLocationOptions =
  DetermineTargetLocationOptionsNoExtraConditions
  | DetermineTargetLocationOptionsStartled
  | DetermineTargetLocationOptionsNotStartledButSmart
  | DetermineTargetLocationOptionsNotStartledButConspiratorial;

export interface DetermineRouteOptions {
  currentLocation: number;
  targetLocation: number;
  isStartled: boolean;
  defs: Readonly<AutopelagoDefinitions>;
  regionIsLocked: Readonly<BitArray>;
  locationIsChecked: Readonly<BitArray>;
}

export type TargetLocationReason =
  'game-not-started'
  | 'nowhere-useful-to-move'
  | 'closest-reachable-unchecked'
  | 'user-requested'
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
  if (options.isStartled) {
    return {
      location: options.defs.startLocation,
      reason: 'startled',
    };
  }

  const victoryLandmarkRegion = options.defs.allRegions[options.victoryLandmarkRegion];
  if ('loc' in victoryLandmarkRegion // always true, but required for the compiler
    && !options.regionIsLocked.at(options.victoryLandmarkRegion)
    && !options.locationIsChecked.at(victoryLandmarkRegion.loc)) {
    return {
      location: victoryLandmarkRegion.loc,
      reason: 'go-mode',
    };
  }

  return {
    location: options.currentLocation,
    reason: 'nowhere-useful-to-move',
  };
}

export function determineRoute(options: Readonly<DetermineRouteOptions>): number[] {
  return [...new Set([options.currentLocation, options.targetLocation])];
}
