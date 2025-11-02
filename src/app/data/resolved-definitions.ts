import { itemClassifications } from 'archipelago.js';

import { stricterIsArray, strictObjectEntries } from '../util';

import * as baked from './baked.json';
import type {
  AutopelagoAura,
  AutopelagoDefinitionsYamlFile,
  YamlBulkItem,
  YamlBulkItemOrGameSpecificItemGroup,
  YamlRequirement,
} from './definitions-file';

export const BAKED_DEFINITIONS = resolveDefinitions(baked as unknown as AutopelagoDefinitionsYamlFile);

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
  allItems: readonly Readonly<AutopelagoItem>[];
  progressionItemsByYamlKey: ReadonlyMap<string, number>;
  itemsWithNonzeroRatCounts: readonly number[];

  allLocations: readonly Readonly<AutopelagoLocation>[];
  allRegions: readonly Readonly<AutopelagoRegion>[];
  startRegion: number;
  startLocation: number;
}

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

export function resolveDefinitions(
  yamlFile: AutopelagoDefinitionsYamlFile,
): AutopelagoDefinitions {
  const allItems: AutopelagoItem[] = [];
  const progressionItemsByYamlKey = new Map<string, number>();

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
    if ('all' in req) {
      return {
        minRequired: 'all',
        children: req.all.map(convertRequirement),
      };
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
    if (key === 'rats' || key === 'useful_nonprogression' || key === 'trap' || key === 'filler' || !item) {
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
      aurasGranted: item.auras_granted ?? [],
      associatedGame: null,
      flavorText: item.flavor_text ?? null,
      ratCount: item.rat_count ?? 0,
    });
  }

  // Process rats (automatically get rat_count = 1)
  for (const [key, item] of strictObjectEntries(yamlFile.items.rats)) {
    if (!item) {
      continue;
    }

    const itemIndex = allItems.length;
    progressionItemsByYamlKey.set(key, itemIndex);

    const [lactoseName, lactoseIntolerantName] = getItemNames(item.name);
    allItems.push({
      key: itemIndex,
      lactoseName,
      lactoseIntolerantName,
      flags: itemClassifications.progression,
      aurasGranted: item.auras_granted ?? [],
      associatedGame: null,
      flavorText: item.flavor_text ?? null,
      ratCount: item.rat_count ?? 1,
    });
  }

  // Helper function to process bulk items
  function processBulkItem(bulkItem: YamlBulkItem, flags: number, associatedGame: string | null): void {
    if (stricterIsArray(bulkItem)) {
      // [name, aurasGranted] format
      const [lactoseName, lactoseIntolerantName] = getItemNames(bulkItem[0]);
      allItems.push({
        key: allItems.length,
        lactoseName,
        lactoseIntolerantName,
        flags,
        aurasGranted: bulkItem[1],
        associatedGame,
        flavorText: null,
        ratCount: 0,
      });
    }
    else {
      // YamlKeyedItem format
      const [lactoseName, lactoseIntolerantName] = getItemNames(bulkItem.name);
      allItems.push({
        key: allItems.length,
        lactoseName,
        lactoseIntolerantName,
        flags,
        aurasGranted: bulkItem.auras_granted ?? [],
        associatedGame,
        flavorText: bulkItem.flavor_text ?? null,
        ratCount: bulkItem.rat_count ?? 0,
      });
    }
  }

  // Helper function to process bulk item or game specific group
  function processBulkItemOrGameSpecific(item: YamlBulkItemOrGameSpecificItemGroup, flags: number): void {
    if ('game_specific' in item) {
      for (const [game, items] of strictObjectEntries(item.game_specific)) {
        if (!items) {
          continue;
        }

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
  const regionNameToIndex = new Map<string, number>();

  // Process landmarks first
  for (const [landmarkKey, landmark] of Object.entries(yamlFile.regions.landmarks)) {
    const regionIndex = allRegions.length;
    regionNameToIndex.set(landmarkKey, regionIndex);

    const locationIndex = allLocations.length;

    // Create the location for this landmark
    allLocations.push({
      key: locationIndex,
      regionLocationKey: [regionIndex, 0], // Landmarks have only one location at index 0
      loc: [0, 0], // Placeholder coordinates
      name: landmark.name,
      flavorText: landmark.flavor_text,
      abilityCheckDC: landmark.ability_check_dc,
      connected: toConnected([], []), // Will be filled later
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

    allRegions.push(landmarkRegion);
  }

  // Process fillers
  for (const [fillerKey, filler] of strictObjectEntries(yamlFile.regions.fillers)) {
    const regionIndex = allRegions.length;
    regionNameToIndex.set(fillerKey, regionIndex);

    // Calculate number of locations based on unrandomized items
    const unrandomizedItems = filler.unrandomized_items;
    let locationCount = 0;

    if (unrandomizedItems.key) {
      for (const keyItem of unrandomizedItems.key) {
        if (typeof keyItem === 'string') {
          locationCount += 1;
        }
        else {
          locationCount += keyItem.count;
        }
      }
    }

    locationCount += (unrandomizedItems.filler ?? 0);
    locationCount += (unrandomizedItems.useful_nonprogression ?? 0);

    // Ensure at least one location
    if (locationCount === 0) {
      throw new Error(`Filler ${fillerKey} has no locations`);
    }

    // Determine ability check DC
    let abilityCheckDC = filler.ability_check_dc;
    if (abilityCheckDC === undefined) {
      // Find the single landmark this filler exits to and subtract 1
      if (filler.exits.length !== 1) {
        throw new Error(`Filler ${fillerKey} should exit to exactly one landmark`);
      }
      const landmarkIndex = regionNameToIndex.get(filler.exits[0]);
      if (landmarkIndex === undefined) {
        throw new Error(`Unknown landmark: ${filler.exits[0]}`);
      }
      const landmarkRegion = allRegions[landmarkIndex];
      abilityCheckDC = landmarkRegion.abilityCheckDC - 1;
    }

    // Create locations for this filler
    const locationIndices: number[] = [];
    for (let i = 0; i < locationCount; i++) {
      const locationIndex = allLocations.length;
      locationIndices.push(locationIndex);

      allLocations.push({
        key: locationIndex,
        regionLocationKey: [regionIndex, i],
        loc: [0, 0], // Placeholder coordinates
        name: filler.name_template.replace('{n}', (i + 1).toString()),
        flavorText: null,
        abilityCheckDC,
        connected: toConnected([], []), // Will be filled later
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
    const landmarkIndex = regionNameToIndex.get(landmarkKey);
    if (landmarkIndex === undefined) {
      continue;
    }

    const landmarkConnections = regionConnections.get(landmarkIndex);
    if (!landmarkConnections) {
      continue;
    }

    // Landmarks can exit to fillers
    if (landmark.exits) {
      for (const exitKey of landmark.exits) {
        const exitIndex = regionNameToIndex.get(exitKey);
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
  for (const [fillerKey, filler] of Object.entries(yamlFile.regions.fillers)) {
    const fillerIndex = regionNameToIndex.get(fillerKey);
    if (fillerIndex === undefined) {
      continue;
    }

    const fillerConnections = regionConnections.get(fillerIndex);
    if (!fillerConnections) {
      continue;
    }

    // Fillers exit to landmarks
    for (const exitKey of filler.exits) {
      const exitIndex = regionNameToIndex.get(exitKey);
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
      if ('loc' in region) {
        // Landmark region
        allRegions[i] = {
          ...region,
          connected,
        };
      }
      else {
        // Filler region
        allRegions[i] = {
          ...region,
          connected,
        };
      }
    }
  }

  // Build location connections based on region connections
  const locationConnections = new Map<number, { forward: number[]; backward: number[] }>();

  // Initialize all locations with empty connections
  for (let i = 0; i < allLocations.length; i++) {
    locationConnections.set(i, { forward: [], backward: [] });
  }

  // Apply the three connection rules
  for (const region of allRegions) {
    if ('loc' in region) {
      // Landmark region - single location
      const locationIndex = region.loc;
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
  const startRegionIndex = regionNameToIndex.get('Menu');
  if (startRegionIndex === undefined) {
    throw new Error('Menu region not found');
  }

  const startRegion = allRegions[startRegionIndex];
  if (!('locs' in startRegion)) {
    throw new Error('Menu region is not a filler');
  }

  const startLocationIndex = startRegion.locs[0];

  return {
    allItems,
    progressionItemsByYamlKey,
    itemsWithNonzeroRatCounts,
    allLocations,
    allRegions,
    startRegion: startRegionIndex,
    startLocation: startLocationIndex,
  };
}
