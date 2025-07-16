import { Component, inject } from '@angular/core';

import { Assets, Sprite, Spritesheet, Texture } from "pixi.js";
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
        interface FrameData {
          frame: { x: number; y: number; w: number; h: number };
          sourceSize: { w: number; h: number };
          spriteSourceSize: { x: number; y: number; w: number; h: number };
        }

        const spritesheetData = {
          frames: {} as Record<string, FrameData>,
          meta: {
            scale: 1
          }
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
        }

        // Create the spritesheet
        const spritesheet = new Spritesheet(spritesheetTexture, spritesheetData);
        await spritesheet.parse();

        // Set up animation cycling between frames
        const frameImages: [s: Sprite, [on1: Texture, on2: Texture], [off1: Texture, off2: Texture]][] = [];

        // Create sprites for each landmark
        for (const [landmarkKey, landmark] of Object.entries(LANDMARKS)) {
          // For now, create "on" sprites - we can add state management later
          const onFrame1 = spritesheet.textures[`${landmarkKey}_on_1`];
          const onFrame2 = spritesheet.textures[`${landmarkKey}_on_2`];
          const offFrame1 = spritesheet.textures[`${landmarkKey}_off_1`];
          const offFrame2 = spritesheet.textures[`${landmarkKey}_off_2`];

          const sprite = new Sprite(onFrame1);

          // Scale down from 64x64 to 16x16 as mentioned in the comment
          sprite.scale.set(0.25);

          // Position using landmark coordinates
          sprite.position.set(landmark.coords[0] - 8, landmark.coords[1] - 8);

          root.addChild(sprite);
          frameImages.push([sprite, [onFrame1, onFrame2], [offFrame1, offFrame2]]);

          // Set the initial value, otherwise a game that starts paused won't get it.
          sprite.texture = onFrame1;
        }

        const context = {
          currentCycleTime: 0,
          currentFrameShown: 0,
          frameImages,
        };
        app.ticker.add(function (t) {
          this.currentCycleTime = (this.currentCycleTime + t.deltaMS) % 1000;
          const currentFrame = Math.floor(this.currentCycleTime * 0.002);
          if (this.currentFrameShown !== currentFrame) {
            this.currentFrameShown = currentFrame;
            for (const [sprite, frames] of this.frameImages) {
              sprite.texture = frames[currentFrame];
            }
          }
        }, context);
      }
    });
  }
}
