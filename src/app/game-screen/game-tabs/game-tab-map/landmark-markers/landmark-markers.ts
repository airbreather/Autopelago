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
      async afterInit(_app, root) {
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
          const offsetY = landmark.sprite_index * 64;

          // OnFrame1 (offsetX: 0)
          spritesheetData.frames[`${landmarkKey}_on_1`] = {
            frame: { x: 0, y: offsetY, w: 64, h: 64 },
            sourceSize: { w: 64, h: 64 },
            spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 }
          };

          // OnFrame2 (offsetX: 64)
          spritesheetData.frames[`${landmarkKey}_on_2`] = {
            frame: { x: 64, y: offsetY, w: 64, h: 64 },
            sourceSize: { w: 64, h: 64 },
            spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 }
          };

          // OffFrame1 (offsetX: 128)
          spritesheetData.frames[`${landmarkKey}_off_1`] = {
            frame: { x: 128, y: offsetY, w: 64, h: 64 },
            sourceSize: { w: 64, h: 64 },
            spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 }
          };

          // OffFrame2 (offsetX: 192)
          spritesheetData.frames[`${landmarkKey}_off_2`] = {
            frame: { x: 192, y: offsetY, w: 64, h: 64 },
            sourceSize: { w: 64, h: 64 },
            spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 }
          };
        }

        // Create the spritesheet
        const spritesheet = new Spritesheet(spritesheetTexture, spritesheetData);
        await spritesheet.parse();

        // Create sprites for each landmark
        for (const [landmarkKey, landmark] of Object.entries(LANDMARKS)) {
          // For now, create "on" sprites - we can add state management later
          const onFrame1 = spritesheet.textures[`${landmarkKey}_on_1`];
          const onFrame2 = spritesheet.textures[`${landmarkKey}_on_2`];

          const sprite = new Sprite(onFrame1);

          // Scale down from 64x64 to 16x16 as mentioned in the comment
          sprite.scale.set(0.25);

          // Position using landmark coordinates
          sprite.position.set(landmark.coords[0], landmark.coords[1]);

          root.addChild(sprite);

          // Set up animation cycling between frames
          let currentFrame = 1;
          setInterval(() => {
            if (currentFrame === 1) {
              sprite.texture = onFrame2;
              currentFrame = 2;
            } else {
              sprite.texture = onFrame1;
              currentFrame = 1;
            }
          }, 500); // Cycle every 500ms
        }
      }
    });
  }
}
