import { effect, type Signal, signal } from '@angular/core';
import { DropShadowFilter } from 'pixi-filters';
import { Container, Graphics } from 'pixi.js';
import {
  type Filler,
  fillerRegionCoords,
  type FillerRegionYamlKey,
  isFillerRegionYamlKey,
} from '../../../../data/locations';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  type VictoryLocationYamlKey,
} from '../../../../data/resolved-definitions';
import type { Set as ImmutableSet } from 'immutable';

import { strictObjectEntries } from '../../../../utils/types';

export interface CreateFillerMarkersOptions {
  victoryLocationYamlKey: Signal<VictoryLocationYamlKey>;
  checkedLocations: Signal<ImmutableSet<number>>;
}

export function createFillerMarkers(options: CreateFillerMarkersOptions) {
  const fillerMarkersSignal = signal<Container | null>(null);
  effect(() => {
    const victoryLocationYamlKey = options.victoryLocationYamlKey();
    const fillerMarkers = new Container({
      filters: [new DropShadowFilter({
        blur: 1,
        offset: { x: 2.4, y: 2.4 },
        color: 'black',
      })],
    });

    const gfx = new Graphics();
    for (const [_, r] of strictObjectEntries(fillerCoordsByRegionLookup[victoryLocationYamlKey])) {
      for (const [x, y] of r.coords) {
        gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
        gfx.fill('yellow');
      }
    }

    fillerMarkers.addChild(gfx);
    fillerMarkersSignal.set(fillerMarkers);
  });

  effect(() => {
    const fillerMarkers = fillerMarkersSignal();
    if (!fillerMarkers) {
      return;
    }

    const victoryLocationYamlKey = options.victoryLocationYamlKey();
    const checkedLocations = options.checkedLocations();
    const defs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
    const coordsByRegion = fillerCoordsByRegionLookup[victoryLocationYamlKey];
    const gfx = new Graphics();
    for (const region of defs.allRegions) {
      if (!('locs' in region)) {
        continue;
      }

      if (!(region.yamlKey in coordsByRegion)) {
        continue;
      }

      const fillerCoords = coordsByRegion[region.yamlKey];
      if (!fillerCoords) {
        continue;
      }

      for (const [i, [x, y]] of fillerCoords.coords.entries()) {
        gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
        gfx.fill(checkedLocations.includes(region.locs[i]) ? 'grey' : 'yellow');
      }
    }

    fillerMarkers.replaceChild(fillerMarkers.children[0], gfx);
  });

  return fillerMarkersSignal;
}

const fillerCoordsByRegionLookup = {
  captured_goldfish: getFillerCoordsByRegion('captured_goldfish'),
  secret_cache: getFillerCoordsByRegion('secret_cache'),
  snakes_on_a_planet: getFillerCoordsByRegion('snakes_on_a_planet'),
} as const satisfies Record<VictoryLocationYamlKey, Partial<Record<FillerRegionYamlKey, Filler>>>;

function getFillerCoordsByRegion(victoryLocation: VictoryLocationYamlKey) {
  const fillerCountsByRegion: Partial<Record<FillerRegionYamlKey, number>> = {};
  for (const r of BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocation].allRegions) {
    if (isFillerRegionYamlKey(r.yamlKey) && 'locs' in r) {
      fillerCountsByRegion[r.yamlKey] = r.locs.length;
    }
  }

  return fillerRegionCoords(fillerCountsByRegion);
}
