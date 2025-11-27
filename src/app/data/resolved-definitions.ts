import BitArray from '@bitarray/typedarray';
import { itemClassifications } from 'archipelago.js';
import Queue from 'yocto-queue';
import {
  MapByCaseInsensitiveString,
  type ReadonlyMapByCaseInsensitiveString,
} from '../utils/map-by-case-insensitive-string';

import { stricterIsArray, strictObjectEntries } from '../utils/types';

import * as baked from './baked.json';
import {
  fillerRegionCoords,
  type FillerRegionYamlKey,
  isFillerRegionYamlKey,
  isLandmarkYamlKey,
  LANDMARKS,
  type LandmarkYamlKey,
  type RegionYamlKey,
} from './locations';

export type AutopelagoBuff =
  | 'well_fed'
  | 'lucky'
  | 'energized'
  | 'stylish'
  | 'smart'
  | 'confident'
  ;

export type AutopelagoTrap =
  | 'upset_tummy'
  | 'unlucky'
  | 'sluggish'
  | 'distracted'
  | 'startled'
  | 'conspiratorial'
  ;

export type AutopelagoAura =
  | AutopelagoBuff
  | AutopelagoTrap
  ;

export interface AutopelagoItem {
  key: number;
  lactoseName: string;
  lactoseIntolerantName: string;
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
  coords: readonly [x: number, y: number];
  name: string;
  flavorText: string | null;
  abilityCheckDC: number;
  connected: Readonly<Connected>;
  unrandomizedProgressionItemYamlKey: string | null;
}

export interface AutopelagoRegionBase {
  key: number;
  yamlKey: FillerRegionYamlKey | LandmarkYamlKey;
  abilityCheckDC: number;
  connected: Readonly<Connected>;
}

export interface AutopelagoLandmarkRegion extends AutopelagoRegionBase {
  yamlKey: LandmarkYamlKey;
  loc: number;
  requirement: AutopelagoRequirement;
}

export interface AutopelagoFillerRegion extends AutopelagoRegionBase {
  yamlKey: FillerRegionYamlKey;
  locs: readonly number[];
}

export type AutopelagoRegion = AutopelagoLandmarkRegion | AutopelagoFillerRegion;

export function getLocs(region: AutopelagoRegion): readonly number[] {
  if ('loc' in region) {
    return [region.loc];
  }
  else {
    return region.locs;
  }
}

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

export type AutopelagoRequirement =
  | AutopelagoRatCountRequirement
  | AutopelagoItemRequirement
  | AutopelagoCompositeRequirement;

export type VictoryLocationYamlKey =
  Extract<LandmarkYamlKey, 'captured_goldfish' | 'secret_cache' | 'snakes_on_a_planet'>;
export type VictoryLocationName =
  | 'Captured Goldfish'
  | 'Secret Cache'
  | 'Snakes on a Planet'
  ;
export const VICTORY_LOCATION_NAME_LOOKUP = {
  'captured_goldfish': 'Captured Goldfish',
  'secret_cache': 'Secret Cache',
  'snakes_on_a_planet': 'Snakes on a Planet',
  'Captured Goldfish': 'captured_goldfish',
  'Secret Cache': 'secret_cache',
  'Snakes on a Planet': 'snakes_on_a_planet',
} as const satisfies (Record<VictoryLocationYamlKey, VictoryLocationName> & Record<VictoryLocationName, VictoryLocationYamlKey>);
export const VICTORY_LOCATION_CROP_LOOKUP = {
  captured_goldfish: 147,
  secret_cache: 300,
  snakes_on_a_planet: 450,
} as const satisfies Record<VictoryLocationYamlKey, number>;

export const BAKED_DEFINITIONS_FULL = resolveMainDefinitions(baked);
export const BAKED_DEFINITIONS_BY_VICTORY_LANDMARK = {
  captured_goldfish: withVictoryLocation(BAKED_DEFINITIONS_FULL, 'captured_goldfish'),
  secret_cache: withVictoryLocation(BAKED_DEFINITIONS_FULL, 'secret_cache'),
  snakes_on_a_planet: withVictoryLocation(BAKED_DEFINITIONS_FULL, 'snakes_on_a_planet'),
} as const;

export interface AutopelagoDefinitions {
  allItems: readonly Readonly<AutopelagoItem>[];
  progressionItemsByYamlKey: ReadonlyMap<string, number>;
  victoryLocationsByYamlKey: ReadonlyMap<VictoryLocationYamlKey, number>;
  itemsWithNonzeroRatCounts: readonly number[];
  itemNameLookup: ReadonlyMap<string, number>;

  allLocations: readonly Readonly<AutopelagoLocation>[];
  allRegions: readonly Readonly<AutopelagoRegion>[];
  regionForLandmarkLocation: readonly number[];
  startRegion: number;
  startLocation: number;
  locationNameLookup: ReadonlyMapByCaseInsensitiveString<number, 'en'>;
}

type YamlRequirement =
  typeof baked.regions.landmarks[keyof typeof baked.regions.landmarks]['requires'];
type YamlBulkItemLevels =
  | 'useful_nonprogression'
  | 'trap'
  | 'filler'
  ;
type GameSpecificBulkItemCategory =
  Extract<typeof baked.items[YamlBulkItemLevels][number], Readonly<Record<'game_specific', unknown>>>['game_specific'];
type YamlBulkItemLookups =
  | Pick<typeof baked.items, YamlBulkItemLevels>[YamlBulkItemLevels]
  | GameSpecificBulkItemCategory[keyof GameSpecificBulkItemCategory];
type YamlBulkItemOrGameSpecificItemGroup = YamlBulkItemLookups[number];
type YamlBulkItem = Exclude<YamlBulkItemOrGameSpecificItemGroup, Readonly<Record<'game_specific', unknown>>>;

function resolveMainDefinitions(
  yamlFile: typeof baked,
): AutopelagoDefinitions {
  const allItems: AutopelagoItem[] = [];
  const progressionItemsByYamlKey = new Map<string, number>();

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

  function getItemNames(name: string | readonly [string, string]): readonly [string, string] {
    return typeof name === 'string'
      ? [name, name]
      : name;
  }

  // Helper function to convert YamlRequirement to AutopelagoRequirement
  function convertRequirement(req: YamlRequirement): AutopelagoRequirement {
    if ('rat_count' in req) {
      return { ratCount: req.rat_count };
    }
    if ('item' in req) {
      const itemIndex = progressionItemsByYamlKey.get(req.item);
      if (itemIndex === undefined) {
        throw new Error(`Unknown item reference: ${req.item}`);
      }
      return { item: itemIndex };
    }
    if ('any' in req) {
      return {
        minRequired: 1,
        children: req.any.map(convertRequirement),
      };
    }
    if ('any_two' in req) {
      return {
        minRequired: 2,
        children: req.any_two.map(convertRequirement),
      };
    }
    throw new Error('Invalid requirement type');
  }

  // Process keyed items (progression items)
  for (const [key, item] of strictObjectEntries(yamlFile.items)) {
    if (key === 'rats' || key === 'useful_nonprogression' || key === 'trap' || key === 'filler') {
      continue;
    }

    const itemIndex = allItems.length;
    progressionItemsByYamlKey.set(key, itemIndex);
    const [lactoseName, lactoseIntolerantName] = getItemNames(item.name);
    allItems.push({
      key: itemIndex,
      lactoseName,
      lactoseIntolerantName,
      flags: itemClassifications.progression, // All keyed items are progression
      aurasGranted: 'auras_granted' in item ? item.auras_granted as AutopelagoAura[] : [],
      associatedGame: null,
      flavorText: 'flavor_text' in item ? item.flavor_text : null,
      ratCount: 'rat_count' in item ? item.rat_count : 0,
    });
  }

  // Process rats (automatically get rat_count = 1)
  for (const [key, item] of strictObjectEntries(yamlFile.items.rats)) {
    const itemIndex = allItems.length;
    progressionItemsByYamlKey.set(key, itemIndex);

    const [lactoseName, lactoseIntolerantName] = getItemNames(item.name);
    allItems.push({
      key: itemIndex,
      lactoseName,
      lactoseIntolerantName,
      flags: itemClassifications.progression,
      aurasGranted: 'auras_granted' in item ? item.auras_granted as AutopelagoAura[] : [],
      associatedGame: null,
      flavorText: 'flavor_text' in item ? item.flavor_text : null,
      ratCount: 1,
    });
  }

  // Helper function to process bulk items
  function processBulkItem(bulkItem: YamlBulkItem, flags: number, associatedGame: string | null): void {
    if (stricterIsArray(bulkItem)) {
      // [name, aurasGranted] format
      const [lactoseName, lactoseIntolerantName] = getItemNames(bulkItem[0] as string | readonly [string, string]);
      allItems.push({
        key: allItems.length,
        lactoseName,
        lactoseIntolerantName,
        flags,
        aurasGranted: bulkItem[1] as readonly AutopelagoAura[],
        associatedGame,
        flavorText: null,
        ratCount: 0,
      });
    }
    else {
      // YamlKeyedItem format
      const [lactoseName, lactoseIntolerantName] = getItemNames(bulkItem.name as string | readonly [string, string]);
      allItems.push({
        key: allItems.length,
        lactoseName,
        lactoseIntolerantName,
        flags,
        aurasGranted: 'auras_granted' in bulkItem ? bulkItem.auras_granted as readonly AutopelagoAura[] : [],
        associatedGame,
        flavorText: 'flavor_text' in bulkItem ? bulkItem.flavor_text : null,
        ratCount: 'rat_count' in bulkItem ? bulkItem.rat_count : 0,
      });
    }
  }

  // Helper function to process bulk item or game specific group
  function processBulkItemOrGameSpecific(item: YamlBulkItemOrGameSpecificItemGroup, flags: number): void {
    if ('game_specific' in item) {
      for (const [game, items] of strictObjectEntries(item.game_specific)) {
        for (const bulkItem of items) {
          processBulkItem(bulkItem, flags, game);
        }
      }
    }
    else {
      processBulkItem(item, flags, null);
    }
  }

  // Process non-progression items
  for (const item of yamlFile.items.useful_nonprogression) {
    processBulkItemOrGameSpecific(item, itemClassifications.useful);
  }

  for (const item of yamlFile.items.trap) {
    processBulkItemOrGameSpecific(item, itemClassifications.trap);
  }

  for (const item of yamlFile.items.filler) {
    processBulkItemOrGameSpecific(item, itemClassifications.none);
  }

  // Calculate items with nonzero rat counts
  const itemsWithNonzeroRatCounts = allItems
    .map((item, index) => ({ item, index }))
    .filter(({ item }) => item.ratCount > 0)
    .map(({ index }) => index);

  // Now process regions and locations
  const allRegions: AutopelagoRegion[] = [];
  const allLocations: AutopelagoLocation[] = [];
  const regionYamlKeyLookup = new Map<RegionYamlKey, number>();
  const victoryLocationsByYamlKey = new Map<VictoryLocationYamlKey, number>();

  // Process landmarks first
  for (const [landmarkKey, landmark] of strictObjectEntries(yamlFile.regions.landmarks)) {
    const regionIndex = allRegions.length;
    regionYamlKeyLookup.set(landmarkKey, regionIndex);

    const locationIndex = allLocations.length;

    // Create the location for this landmark
    allLocations.push({
      key: locationIndex,
      regionLocationKey: [regionIndex, 0], // Landmarks have only one location at index 0
      coords: LANDMARKS[landmarkKey].coords,
      name: landmark.name,
      flavorText: landmark.flavor_text,
      abilityCheckDC: landmark.ability_check_dc,
      connected: toConnected([], []), // Will be filled later
      unrandomizedProgressionItemYamlKey: landmark.unrandomized_item,
    });

    // Create the landmark region
    const landmarkRegion: AutopelagoLandmarkRegion = {
      key: regionIndex,
      yamlKey: landmarkKey,
      abilityCheckDC: landmark.ability_check_dc,
      connected: toConnected([], []), // Will be filled later
      loc: locationIndex,
      requirement: convertRequirement(landmark.requires),
    };

    switch (landmarkKey) {
      case 'captured_goldfish':
      case 'secret_cache':
      case 'snakes_on_a_planet':
        victoryLocationsByYamlKey.set(landmarkKey, locationIndex);
        break;
    }

    allRegions.push(landmarkRegion);
  }

  // Process fillers
  const locationCountByFillerRegion: Partial<Record<FillerRegionYamlKey, number>> = {};
  for (const [fillerKey, filler] of strictObjectEntries(yamlFile.regions.fillers)) {
    const regionIndex = allRegions.length;
    regionYamlKeyLookup.set(fillerKey, regionIndex);

    // Calculate number of locations based on unrandomized items
    const unrandomizedItems = filler.unrandomized_items;
    const progressionItemYamlKeys: (string | null)[] = [];

    if ('key' in unrandomizedItems) {
      for (const keyItem of unrandomizedItems.key) {
        if (typeof keyItem === 'object') {
          for (let i = 0; i < keyItem.count; i++) {
            progressionItemYamlKeys.push(keyItem.item);
          }
        }
        else {
          progressionItemYamlKeys.push(keyItem);
        }
      }
    }

    if ('filler' in unrandomizedItems) {
      for (let i = 0; i < unrandomizedItems.filler; i++) {
        progressionItemYamlKeys.push(null);
      }
    }

    if ('useful_nonprogression' in unrandomizedItems) {
      for (let i = 0; i < unrandomizedItems.useful_nonprogression; i++) {
        progressionItemYamlKeys.push(null);
      }
    }

    // Ensure at least one location
    if (progressionItemYamlKeys.length === 0) {
      throw new Error(`Filler ${fillerKey} has no locations`);
    }

    locationCountByFillerRegion[fillerKey] = progressionItemYamlKeys.length;

    // Determine ability check DC
    let abilityCheckDC: number;
    if ('ability_check_dc' in filler) {
      abilityCheckDC = filler.ability_check_dc;
    }
    else {
      // Find the single landmark this filler exits to and subtract 1
      if (filler.exits.length !== 1 || !isLandmarkYamlKey(filler.exits[0])) {
        throw new Error(`Filler ${fillerKey} should exit to exactly one landmark`);
      }
      const landmarkIndex = regionYamlKeyLookup.get(filler.exits[0]);
      if (landmarkIndex === undefined) {
        throw new Error(`Unknown landmark: ${filler.exits[0]}`);
      }
      const landmarkRegion = allRegions[landmarkIndex];
      abilityCheckDC = landmarkRegion.abilityCheckDC - 1;
    }

    // Create locations for this filler
    const locationIndices: number[] = [];
    for (let i = 0; i < progressionItemYamlKeys.length; i++) {
      const locationIndex = allLocations.length;
      locationIndices.push(locationIndex);

      allLocations.push({
        key: locationIndex,
        regionLocationKey: [regionIndex, i],
        coords: [0, 0], // Placeholder coordinates
        name: filler.name_template.replace('{n}', (i + 1).toString()),
        flavorText: null,
        abilityCheckDC,
        connected: toConnected([], []), // Will be filled later
        unrandomizedProgressionItemYamlKey: progressionItemYamlKeys[i],
      });
    }

    // Create the filler region
    const fillerRegion: AutopelagoFillerRegion = {
      key: regionIndex,
      yamlKey: fillerKey,
      abilityCheckDC,
      connected: toConnected([], []), // Will be filled later
      locs: locationIndices,
    };

    allRegions.push(fillerRegion);
  }

  // Build connection maps
  const regionConnections = new Map<number, { forward: number[]; backward: number[] }>();

  // Initialize all regions with empty connections
  for (let i = 0; i < allRegions.length; i++) {
    regionConnections.set(i, { forward: [], backward: [] });
  }

  // Process landmark exits
  for (const [landmarkKey, landmark] of strictObjectEntries(yamlFile.regions.landmarks)) {
    const landmarkIndex = regionYamlKeyLookup.get(landmarkKey);
    if (landmarkIndex === undefined) {
      continue;
    }

    const landmarkConnections = regionConnections.get(landmarkIndex);
    if (!landmarkConnections) {
      continue;
    }

    // Landmarks can exit to fillers
    if ('exits' in landmark) {
      for (const exitKey of landmark.exits) {
        if (!isFillerRegionYamlKey(exitKey)) {
          throw new Error(`Landmark ${landmarkKey} exits to invalid region: ${exitKey}`);
        }

        const exitIndex = regionYamlKeyLookup.get(exitKey);
        if (exitIndex !== undefined) {
          landmarkConnections.forward.push(exitIndex);
          const targetConnections = regionConnections.get(exitIndex);
          if (targetConnections) {
            targetConnections.backward.push(landmarkIndex);
          }
        }
      }
    }
  }

  // Process filler exits
  for (const [fillerKey, filler] of strictObjectEntries(yamlFile.regions.fillers)) {
    const fillerIndex = regionYamlKeyLookup.get(fillerKey);
    if (fillerIndex === undefined) {
      continue;
    }

    const fillerConnections = regionConnections.get(fillerIndex);
    if (!fillerConnections) {
      continue;
    }

    // Fillers exit to landmarks
    for (const exitKey of filler.exits) {
      if (!isLandmarkYamlKey(exitKey)) {
        throw new Error(`Filler ${fillerKey} exits to invalid region: ${exitKey}`);
      }
      const exitIndex = regionYamlKeyLookup.get(exitKey);
      if (exitIndex !== undefined) {
        fillerConnections.forward.push(exitIndex);
        const targetConnections = regionConnections.get(exitIndex);
        if (targetConnections) {
          targetConnections.backward.push(fillerIndex);
        }
      }
    }
  }

  // Apply connections to regions
  for (let i = 0; i < allRegions.length; i++) {
    const connections = regionConnections.get(i);
    if (connections) {
      const region = allRegions[i];
      const connected = toConnected(connections.forward, connections.backward);
      // Create a new region object with updated connections
      allRegions[i] = {
        ...region,
        connected,
      };
    }
  }

  // Build location connections based on region connections
  const locationConnections = new Map<number, { forward: number[]; backward: number[] }>();

  // Initialize all locations with empty connections
  for (let i = 0; i < allLocations.length; i++) {
    locationConnections.set(i, { forward: [], backward: [] });
  }

  // Apply the three connection rules
  const regionForLandmarkLocation = Array<number>(allLocations.length).fill(NaN);
  for (const region of allRegions) {
    if ('loc' in region) {
      // Landmark region - single location
      const locationIndex = region.loc;
      regionForLandmarkLocation[region.loc] = region.key;
      const locationConns = locationConnections.get(locationIndex);
      if (!locationConns) {
        continue;
      }

      // Rule 1: Last (only) location of landmark connects to first location of each exit region
      for (const exitRegionIndex of region.connected.forward) {
        const exitRegion = allRegions[exitRegionIndex];
        let firstLocationIndex: number;

        if ('loc' in exitRegion) {
          // Exit to landmark - connect to its single location
          firstLocationIndex = exitRegion.loc;
        }
        else {
          // Exit to filler - connect to its first location
          firstLocationIndex = exitRegion.locs[0];
        }

        locationConns.forward.push(firstLocationIndex);
        const targetLocationConns = locationConnections.get(firstLocationIndex);
        if (targetLocationConns) {
          targetLocationConns.backward.push(locationIndex);
        }
      }
    }
    else {
      // Filler region - multiple locations
      const locations = region.locs;

      // Rule 2: Each location (except last) connects to next location in same region
      for (let i = 0; i < locations.length - 1; i++) {
        const currentLocationIndex = locations[i];
        const nextLocationIndex = locations[i + 1];

        const currentLocationConns = locationConnections.get(currentLocationIndex);
        const nextLocationConns = locationConnections.get(nextLocationIndex);

        if (currentLocationConns && nextLocationConns) {
          currentLocationConns.forward.push(nextLocationIndex);
          nextLocationConns.backward.push(currentLocationIndex);
        }
      }

      // Rule 1: Last location connects to first location of each exit region
      if (locations.length > 0) {
        const lastLocationIndex = locations[locations.length - 1];
        const lastLocationConns = locationConnections.get(lastLocationIndex);
        if (!lastLocationConns) {
          continue;
        }

        for (const exitRegionIndex of region.connected.forward) {
          const exitRegion = allRegions[exitRegionIndex];
          let firstLocationIndex: number;

          if ('loc' in exitRegion) {
            // Exit to landmark - connect to its single location
            firstLocationIndex = exitRegion.loc;
          }
          else {
            // Exit to filler - connect to its first location
            firstLocationIndex = exitRegion.locs[0];
          }

          lastLocationConns.forward.push(firstLocationIndex);
          const targetLocationConns = locationConnections.get(firstLocationIndex);
          if (targetLocationConns) {
            targetLocationConns.backward.push(lastLocationIndex);
          }
        }
      }
    }
  }

  // Apply connections to locations
  for (let i = 0; i < allLocations.length; i++) {
    const connections = locationConnections.get(i);
    if (connections) {
      const location = allLocations[i];
      const connected = toConnected(connections.forward, connections.backward);
      allLocations[i] = {
        ...location,
        connected,
      };
    }
  }

  // Find start region and location
  const startRegionIndex = regionYamlKeyLookup.get('Menu');
  if (startRegionIndex === undefined) {
    throw new Error('Menu region not found');
  }

  const startRegion = allRegions[startRegionIndex];
  if (!('locs' in startRegion)) {
    throw new Error('Menu region is not a filler');
  }

  const startLocationIndex = startRegion.locs[0];

  // build name lookups
  const itemNameLookup = new Map<string, number>();
  for (const [itemIndex, item] of allItems.entries()) {
    itemNameLookup.set(item.lactoseName, itemIndex);
    itemNameLookup.set(item.lactoseIntolerantName, itemIndex);
  }

  const locationNameLookup = new MapByCaseInsensitiveString<number>('en');
  for (const [locationIndex, location] of allLocations.entries()) {
    locationNameLookup.set(location.name, locationIndex);
  }

  const fillerCoordsLookup = fillerRegionCoords(locationCountByFillerRegion);
  for (const region of allRegions) {
    if ('loc' in region) {
      continue;
    }

    const fillerCoords = fillerCoordsLookup[region.yamlKey];
    if (!fillerCoords) {
      throw new Error(`Filler region ${region.yamlKey} has no coordinates. this is a programming error.`);
    }

    for (const [coordIndex, loc] of region.locs.entries()) {
      allLocations[loc].coords = fillerCoords.coords[coordIndex];
    }
  }

  return {
    allItems,
    progressionItemsByYamlKey,
    victoryLocationsByYamlKey,
    itemsWithNonzeroRatCounts,
    allLocations,
    allRegions,
    regionForLandmarkLocation,
    startRegion: startRegionIndex,
    startLocation: startLocationIndex,
    itemNameLookup,
    locationNameLookup,
  };
}

function withVictoryLocation(defs: AutopelagoDefinitions, location: VictoryLocationYamlKey): AutopelagoDefinitions {
  const allLocations: AutopelagoLocation[] = [];
  const oldToNewLocationMap = new Map<number, number>();
  const allRegions: AutopelagoRegion[] = [];
  const oldToNewRegionMap = new Map<number, number>();

  const regionsEnqueuedBefore = new BitArray(defs.allRegions.length);
  const q = new Queue<number>();

  function enqueueRegion(regionIndex: number) {
    if (regionsEnqueuedBefore[regionIndex]) {
      return;
    }

    regionsEnqueuedBefore[regionIndex] = 1;
    q.enqueue(regionIndex);
  }

  enqueueRegion(defs.startRegion);
  for (let regionIndex = q.dequeue(); regionIndex !== undefined; regionIndex = q.dequeue()) {
    oldToNewRegionMap.set(regionIndex, allRegions.length);
    const region = {
      ...defs.allRegions[regionIndex],
      key: allRegions.length,
    };
    allRegions.push(region);
    for (const loc of getLocs(region)) {
      oldToNewLocationMap.set(loc, allLocations.length);
      const newLocation = {
        ...defs.allLocations[loc],
        key: allLocations.length,
      };
      allLocations.push(newLocation);
    }

    let nextRegions = region.connected.all;
    if (region.yamlKey === location) {
      nextRegions = nextRegions.filter(c => c[1] !== 'forward');
    }

    for (const [exit] of nextRegions) {
      enqueueRegion(exit);
    }
  }

  // fix connections
  function remapConnected(conn: Connected, map: ReadonlyMap<number, number>): Connected {
    const forward: number[] = [];
    const backward: number[] = [];
    const all: [number, 'forward' | 'backward'][] = [];
    for (const [exit, direction] of conn.all) {
      const newExit = map.get(exit);
      if (newExit === undefined) {
        continue;
      }

      if (direction === 'forward') {
        forward.push(newExit);
      }
      else {
        backward.push(newExit);
      }
      all.push([newExit, direction]);
    }

    return { forward, backward, all };
  }

  for (let oldRegionIndex = 0; oldRegionIndex < defs.allRegions.length; oldRegionIndex++) {
    const newRegionIndex = oldToNewRegionMap.get(oldRegionIndex);
    if (newRegionIndex === undefined) {
      continue;
    }

    const oldRegion = defs.allRegions[oldRegionIndex];
    const newConnected = remapConnected(oldRegion.connected, oldToNewRegionMap);

    const newLocs: number[] = [];
    for (const oldLocIndex of getLocs(oldRegion)) {
      const newLocIndex = oldToNewLocationMap.get(oldLocIndex);
      if (newLocIndex === undefined) {
        // shouldn't happen, but whatever.
        continue;
      }

      newLocs.push(newLocIndex);
      const oldLoc = defs.allLocations[oldLocIndex];
      allLocations[newLocIndex] = {
        ...oldLoc,
        key: newLocIndex,
        connected: remapConnected(oldLoc.connected, oldToNewLocationMap),
        // if it ever helps, the following oldLoc.regionLocationKey[1] can be replaced by the
        // index of the current position within the getLocs() array.
        regionLocationKey: [newRegionIndex, oldLoc.regionLocationKey[1]],
      };
    }
    if ('loc' in oldRegion) {
      allRegions[newRegionIndex] = {
        ...oldRegion,
        key: newRegionIndex,
        connected: newConnected,
        loc: newLocs[0],
      };
    }
    else {
      allRegions[newRegionIndex] = {
        ...oldRegion,
        key: newRegionIndex,
        connected: newConnected,
        locs: newLocs,
      };
    }
  }

  const newStartRegion = oldToNewRegionMap.get(defs.startRegion);
  const newStartLocation = oldToNewLocationMap.get(defs.startLocation);
  if (newStartRegion === undefined || newStartLocation === undefined) {
    throw new Error('Failed to find remapped start region or location');
  }

  const locationNameLookup = new MapByCaseInsensitiveString<number>('en');
  for (const [locationIndex, location] of allLocations.entries()) {
    locationNameLookup.set(location.name, locationIndex);
  }

  const newVictoryLocationsByYamlKey = new Map<VictoryLocationYamlKey, number>();
  for (const [locationKey, locationIndex] of defs.victoryLocationsByYamlKey.entries()) {
    const newLocationIndex = oldToNewLocationMap.get(locationIndex);
    if (newLocationIndex !== undefined) {
      newVictoryLocationsByYamlKey.set(locationKey, newLocationIndex);
    }
  }

  const newRegionForLandmarkLocation = Array<number>(allLocations.length).fill(NaN);
  for (const newRegion of allRegions) {
    if ('loc' in newRegion) {
      newRegionForLandmarkLocation[newRegion.loc] = newRegion.key;
    }
  }

  return {
    ...defs,
    allLocations,
    allRegions,
    regionForLandmarkLocation: newRegionForLandmarkLocation,
    startRegion: newStartRegion,
    startLocation: newStartLocation,
    locationNameLookup,
    victoryLocationsByYamlKey: newVictoryLocationsByYamlKey,
  };
}
