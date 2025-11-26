import { computed, type Signal } from '@angular/core';
import { DropShadowFilter } from 'pixi-filters';
import { Container, Graphics } from 'pixi.js';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  type VictoryLocationYamlKey,
} from '../../../../data/resolved-definitions';

export interface CreateFillerMarkersOptions {
  victoryLocationYamlKey: Signal<VictoryLocationYamlKey>;
}

export interface FillerMarkers {
  container: Container;
  markChecked(...locs: readonly number[]): void;
}

export function createFillerMarkers(options: CreateFillerMarkersOptions): Signal<FillerMarkers> {
  return computed(() => {
    const victoryLocationYamlKey = options.victoryLocationYamlKey();
    const fillerMarkers = new Container({
      filters: [new DropShadowFilter({
        blur: 1,
        offset: { x: 2.4, y: 2.4 },
        color: 'black',
      })],
    });

    const { allLocations, regionForLandmarkLocation } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
    const color = Array<'yellow' | 'grey'>(allLocations.length).fill('yellow');
    let prevGfx = new Graphics();
    fillerMarkers.addChild(prevGfx);
    function redraw() {
      const gfx = new Graphics();
      for (let i = 0; i < allLocations.length; i++) {
        if (Number.isNaN(regionForLandmarkLocation[i])) {
          const [x, y] = allLocations[i].coords;
          gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
          gfx.fill(color[i]);
        }
      }
      fillerMarkers.replaceChild(prevGfx, gfx);
      prevGfx = gfx;
    }
    redraw();
    const markChecked = (...locs: readonly number[]) => {
      let updatedAtLeastOne = false;
      for (const loc of locs) {
        if (color[loc] === 'grey') {
          continue;
        }
        color[loc] = 'grey';
        updatedAtLeastOne = true;
      }

      if (updatedAtLeastOne) {
        redraw();
      }
    };

    return {
      container: fillerMarkers,
      markChecked,
    };
  });
}
