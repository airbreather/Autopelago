import { itemClassifications } from 'archipelago.js';

import { strictObjectEntries } from '../util';
import { AutopelagoAura, AutopelagoDefinitionsYamlFile, YamlRequirement } from './definitions-file';

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
  progressionItemsByYamlKey: ReadonlyMap<string, number>;
  itemsWithNonzeroRatCounts: readonly number[];

  allLocations: readonly AutopelagoLocation[];
  allRegions: readonly AutopelagoRegion[];
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
  useLactoseNames: boolean,
): AutopelagoDefinitions {
  const allItems: AutopelagoItem[] = [];
  const progressionItemsByYamlKey = new Map<string, number>();

  // Helper function to get item name based on lactose preference
  function getItemName(name: string | readonly [string, string]): string {
    if (typeof name === 'string') {
      return name;
    }
    return useLactoseNames ? name[0] : name[1];
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

    allItems.push({
      key: itemIndex,
      name: getItemName(item.name),
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

    allItems.push({
      key: itemIndex,
      name: getItemName(item.name),
      flags: itemClassifications.progression,
      aurasGranted: item.auras_granted ?? [],
      associatedGame: null,
      flavorText: item.flavor_text ?? null,
      ratCount: item.rat_count ?? 1,
    });
  }

  // Helper function to process bulk items
  function processBulkItem(bulkItem: unknown, associatedGame: string | null): void {
    if (Array.isArray(bulkItem)) {
      // [name, aurasGranted] format
      const arrayItem = bulkItem as [string | readonly [string, string], readonly AutopelagoAura[]];
      allItems.push({
        key: allItems.length,
        name: getItemName(arrayItem[0]),
        flags: 0, // Non-progression items have no flags by default
        aurasGranted: arrayItem[1],
        associatedGame,
        flavorText: null,
        ratCount: 0,
      });
    }
    else {
      // YamlKeyedItem format
      const keyedItem = bulkItem as { name: string | readonly [string, string]; auras_granted?: readonly AutopelagoAura[]; flavor_text?: string; rat_count?: number };
      allItems.push({
        key: allItems.length,
        name: getItemName(keyedItem.name),
        flags: 0,
        aurasGranted: keyedItem.auras_granted ?? [],
        associatedGame,
        flavorText: keyedItem.flavor_text ?? null,
        ratCount: keyedItem.rat_count ?? 0,
      });
    }
  }

  // Helper function to process bulk item or game specific group
  function processBulkItemOrGameSpecific(item: unknown): void {
    const typedItem = item as { game_specific?: Record<string, unknown[]> };
    if ('game_specific' in typedItem && typedItem.game_specific) {
      for (const [game, items] of Object.entries(typedItem.game_specific)) {
        for (const bulkItem of items) {
          processBulkItem(bulkItem, game);
        }
      }
    }
    else {
      processBulkItem(item, null);
    }
  }

  // Process non-progression items
  for (const item of yamlFile.items.useful_nonprogression) {
    processBulkItemOrGameSpecific(item);
  }

  for (const item of yamlFile.items.trap) {
    processBulkItemOrGameSpecific(item);
  }

  for (const item of yamlFile.items.filler) {
    processBulkItemOrGameSpecific(item);
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

  // Find start region and location
  const startRegionIndex = regionNameToIndex.get('Menu');
  if (startRegionIndex === undefined) {
    throw new Error('Menu region not found');
  }

  const startRegion = allRegions[startRegionIndex] as AutopelagoFillerRegion;
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
