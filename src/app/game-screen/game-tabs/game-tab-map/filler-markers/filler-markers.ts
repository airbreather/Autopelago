import { Component, inject } from '@angular/core';
import YAML from 'yaml';
import { AutopelagoDefinitionsYamlFile } from '../../../../data/definitions-file';
import { FillerRegionName, fillerRegions } from '../../../../data/locations';
import { strictObjectEntries } from '../../../../util';
import { PixiService } from '../pixi-service';
import { Container, Graphics } from 'pixi.js';
import { DropShadowFilter } from 'pixi-filters';

@Component({
  selector: 'app-filler-markers',
  imports: [],
  template: '',
  styles: '',
})
export class FillerMarkers {
  constructor() {
    const loadCounts = this.#loadCounts();
    inject(PixiService).registerPlugin({
      async afterInit(_app, root) {
        const counts = await loadCounts;
        const graphicsContainer = new Container({
          filters: [new DropShadowFilter({
            blur: 1,
            offset: { x: 2.4, y: 2.4 },
            color: 'black',
          })],
        });
        root.addChild(graphicsContainer);
        const gfx = new Graphics();
        for (const [_, { coords }] of strictObjectEntries(fillerRegions(counts))) {
          for (const [x, y] of coords) {
            gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
            gfx.fill('yellow');
          }
        }

        graphicsContainer.addChild(gfx);
      },
    });
  }

  async #loadCounts() {
    const defsYml = await (await fetch('/assets/AutopelagoDefinitions.yml')).text();
    const defs = YAML.parse(defsYml) as AutopelagoDefinitionsYamlFile;
    const result: Partial<Record<FillerRegionName, number>> = {};
    for (const [name, filler] of strictObjectEntries(defs.regions.fillers)) {
      result[name] =
        (filler.unrandomized_items.filler ?? 0)
        + (filler.unrandomized_items.useful_nonprogression ?? 0)
        + (filler.unrandomized_items.key?.length ?? 0);
    }

    return result as Record<FillerRegionName, number>;
  }
}
