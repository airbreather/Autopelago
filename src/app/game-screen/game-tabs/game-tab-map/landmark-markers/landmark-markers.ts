import { Component, inject } from '@angular/core';

import { AnimatedSprite, Assets, Spritesheet, SpritesheetData, Texture } from "pixi.js";
import { PixiService } from "../pixi-service";
import { LANDMARKS } from "../../../../data/locations";

@Component({
  selector: 'app-landmark-markers',
  imports: [],
  template: `
  `,
  styles: ``,
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class LandmarkMarkers {
  constructor() {
    let loadSpritesheetTexture: Promise<Texture> | null = null;
    inject(PixiService).registerPlugin({
      beforeInit() {
        loadSpritesheetTexture = Assets.load<Texture>('/assets/images/locations.webp');
      },
      async afterInit(app, root) {
        if (!loadSpritesheetTexture) {
          throw new Error("beforeInit() must finish before afterInit() may start");
        }

        const spritesheetTexture = await loadSpritesheetTexture;

        // Create spritesheet data with frame definitions for each landmark
        const spritesheetData: SpritesheetData & Required<Pick<SpritesheetData, 'animations'>> = {
          frames: {},
          meta: { scale: 4 },
          animations: {},
        };

        // Generate frame definitions for each landmark
        for (const [landmarkKey, landmark] of Object.entries(LANDMARKS)) {
          const offsetY = landmark.sprite_index * 65;

          // OnFrame1 (offsetX: 0)
          spritesheetData.frames[`${landmarkKey}_on_1`] = {
            frame: { x: 0, y: offsetY, w: 64, h: 64 },
            sourceSize: { w: 64, h: 64 },
            spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 }
          };

          // OnFrame2 (offsetX: 64)
          spritesheetData.frames[`${landmarkKey}_on_2`] = {
            frame: { x: 65, y: offsetY, w: 64, h: 64 },
            sourceSize: { w: 64, h: 64 },
            spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 }
          };

          // OffFrame1 (offsetX: 128)
          spritesheetData.frames[`${landmarkKey}_off_1`] = {
            frame: { x: 130, y: offsetY, w: 64, h: 64 },
            sourceSize: { w: 64, h: 64 },
            spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 }
          };

          // OffFrame2 (offsetX: 192)
          spritesheetData.frames[`${landmarkKey}_off_2`] = {
            frame: { x: 195, y: offsetY, w: 64, h: 64 },
            sourceSize: { w: 64, h: 64 },
            spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 }
          };

          spritesheetData.animations[`${landmarkKey}_on`] = [`${landmarkKey}_on_1`, `${landmarkKey}_on_2`];
          spritesheetData.animations[`${landmarkKey}_off`] = [`${landmarkKey}_off_1`, `${landmarkKey}_off_2`];
        }

        // Create the spritesheet
        const spritesheet = new Spritesheet(spritesheetTexture, spritesheetData);
        await spritesheet.parse();

        // Create sprites for each landmark
        for (const [landmarkKey, landmark] of Object.entries(LANDMARKS)) {
          const anim = new AnimatedSprite(spritesheet.animations[`${landmarkKey}_on`]);
          anim.animationSpeed = 2 / app.ticker.FPS;
          anim.position.set(landmark.coords[0] - 8, landmark.coords[1] - 8);
          anim.play();
          root.addChild(anim);
        }
      }
    });
  }
}
