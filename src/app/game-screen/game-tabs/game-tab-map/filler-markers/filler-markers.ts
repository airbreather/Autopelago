import { Component, computed, effect, inject, signal } from '@angular/core';

import { Application, Container, Graphics } from 'pixi.js';

import { DropShadowFilter } from 'pixi-filters';

import { PixiService } from '../pixi-service';

import { fillerRegions } from '../../../../data/locations';
import { GameDefinitionsStore } from '../../../../store/game-definitions-store';
import { strictObjectEntries } from '../../../../util';

@Component({
  selector: 'app-filler-markers',
  imports: [],
  template: '',
  styles: '',
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class FillerMarkers {
  constructor() {
    const store = inject(GameDefinitionsStore);
    const graphicsContainer = new Container({
      filters: [new DropShadowFilter({
        blur: 1,
        offset: { x: 2.4, y: 2.4 },
        color: 'black',
      })],
    });
    const graphics = computed(() => {
      const counts = store.fillerCountsByRegion();
      if (!counts) {
        return null;
      }

      const gfx = new Graphics();
      for (const [_, { coords }] of strictObjectEntries(fillerRegions(counts))) {
        for (const [x, y] of coords) {
          gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
          gfx.fill('yellow');
        }
      }

      return gfx;
    });
    const pixiApp = signal<Application | null>(null);
    effect(() => {
      const app = pixiApp();
      if (!app) {
        return;
      }

      const gfx = graphics();
      if (graphicsContainer.children.length > 0) {
        if (gfx) {
          graphicsContainer.replaceChild(graphicsContainer.children[0], gfx);
        }
        else {
          graphicsContainer.removeChildAt(0);
        }
      }
      else if (gfx) {
        graphicsContainer.addChild(gfx);
      }
    });

    inject(PixiService).registerPlugin({
      afterInit(app, root) {
        root.addChild(graphicsContainer);
        pixiApp.set(app);
      },
    });
  }
}
