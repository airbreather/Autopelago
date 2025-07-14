import { Component, inject } from '@angular/core';

import { Assets, Sprite, Texture } from "pixi.js";
import { PixiService } from "../pixi-service";

@Component({
  selector: 'app-landmark-markers',
  imports: [],
  template: `
  `,
  styles: `
    .quest-marker {
      position: absolute;
      width: calc(100% * 12 / 300);
      height: calc(100% * 12 / 450);
    }

    .landmark {
      position: absolute;
      width: calc(100% * 16 / 300);
      height: calc(100% * 16 / 450);

      &.unchecked {
        filter: drop-shadow(8px 16px 16px black) saturate(10%) brightness(40%);
      }

      &:not(.unchecked) {
        filter: drop-shadow(8px 16px 16px black);
      }
    }
  `,
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class LandmarkMarkers {
  constructor() {
    let loadTexture: Promise<Texture> | null = null;
    inject(PixiService).registerPlugin({
      beforeInit() {
        loadTexture = Assets.load<Texture>('/assets/images/locations/binary_tree.webp');
      },
      async afterInit(_app, root) {
        if (!loadTexture) {
          throw new Error("beforeInit() must finish before afterInit() may start");
        }

        const sprite = new Sprite(await loadTexture);
        sprite.scale.set(0.25);
        sprite.position.set(200, 150);
        root.addChild(sprite);
      }
    });
  }
}
