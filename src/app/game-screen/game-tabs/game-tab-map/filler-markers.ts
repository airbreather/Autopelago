import { Container, Graphics } from 'pixi.js';

import { DropShadowFilter } from 'pixi-filters';

import { FillerRegionName, fillerRegions } from '../../../data/locations';
import { strictObjectEntries } from '../../../util';

export function createFillerMarkers(fillerCountsByRegion: Readonly<Partial<Record<FillerRegionName, number>>>) {
  const graphicsContainer = new Container({
    filters: [new DropShadowFilter({
      blur: 1,
      offset: { x: 2.4, y: 2.4 },
      color: 'black',
    })],
  });

  const gfx = new Graphics();
  for (const [_, r] of strictObjectEntries(fillerRegions(fillerCountsByRegion))) {
    for (const [x, y] of r.coords) {
      gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
      gfx.fill('yellow');
    }
  }

  graphicsContainer.addChild(gfx);
  return graphicsContainer;
}
