import { AutopelagoAura } from './definitions-file';

export interface AutopelagoItem {
  key: number;
  name: string;
  flags: number;
  aurasGranted: readonly AutopelagoAura[];
  associatedGame: string | null;
  flavorText: string | null;
  ratCount: number;
}

interface Connected {
  forward: readonly number[];
  backward: readonly number[];
  all: readonly (readonly [x: number, dir: 'forward' | 'backward'])[];
}

export interface AutopelagoLocation {
  key: number;
  regionLocationKey: readonly [region: number, n: number];
  loc: readonly [x: number, y: number];
  name: string;
  flavorText: string | null;
  abilityCheckDC: number;
  connected: Readonly<Connected>;
}

export interface AutopelagoRegionBase {
  key: number;
  yamlKey: string;
  abilityCheckDC: number;
  connected: Readonly<Connected>;
}

export interface AutopelagoLandmarkRegion extends AutopelagoRegionBase {
  loc: number;
  requirement: AutopelagoRequirement;
}

export interface AutopelagoFillerRegion extends AutopelagoRegionBase {
  locs: readonly number[];
}

export type AutopelagoRegion = AutopelagoLandmarkRegion | AutopelagoFillerRegion;

export interface AutopelagoRatCountRequirement {
  ratCount: number;
}

export interface AutopelagoItemRequirement {
  item: number;
}

export interface AutopelagoCompositeRequirement {
  minRequired: 1 | 2 | 'all';
  children: readonly AutopelagoRequirement[];
}

export type AutopelagoRequirement = AutopelagoRatCountRequirement
  | AutopelagoItemRequirement
  | AutopelagoCompositeRequirement;

export interface AutopelagoDefinitions {
  allItems: readonly AutopelagoItem[];
  allLocations: readonly AutopelagoLocation[];
  allRegions: readonly AutopelagoRegion[];
}

/* eslint-disable @typescript-eslint/no-unused-vars */
// noinspection JSUnusedLocalSymbols
function toConnected(forward: readonly number[], backward: readonly number[]): Connected {
  return {
    forward,
    backward,
    all: [
      ...forward.map(x => [x, 'forward'] as const),
      ...backward.map(x => [x, 'backward'] as const),
    ],
  };
}
/* eslint-enable @typescript-eslint/no-unused-vars */
