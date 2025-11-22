import { effect, resource, type Signal } from '@angular/core';
import { DropShadowFilter } from 'pixi-filters';
import { AnimatedSprite, Assets, Container, Sprite, Spritesheet, type SpritesheetData, Texture, Ticker } from 'pixi.js';
import { LANDMARKS, type LandmarkYamlKey } from '../../../../data/locations';
import { BAKED_DEFINITIONS_BY_VICTORY_LANDMARK } from '../../../../data/resolved-definitions';
import type { GameStore } from '../../../../store/autopelago-store';
import { strictObjectEntries } from '../../../../util';

export interface CreateLandmarkMarkersOptions {
  store: InstanceType<typeof GameStore>;
  enableTileAnimationsSignal: Signal<boolean | null>;
}

export function createLandmarkMarkers({ store, enableTileAnimationsSignal }: CreateLandmarkMarkersOptions) {
  const landmarksResource = resource({
    defaultValue: null,
    params: () => enableTileAnimationsSignal(),
    loader: async ({ params: enableTileAnimations }) => {
      if (enableTileAnimations === null) {
        return null;
      }

      const spritesheetTexture = await Assets.load<Texture>('assets/images/locations.webp');
      const spritesheet = new Spritesheet(spritesheetTexture, spritesheetData);
      await spritesheet.parse();
      const victoryLocationYamlKey = store.victoryLocationYamlKey();
      const container = new Container({
        filters: [new DropShadowFilter({
          blur: 1,
          offset: { x: 11.2, y: 11.2 },
          color: 'black',
        })],
      });

      // Create sprites for each landmark
      const spriteLookup: Partial<Record<LandmarkYamlKey, SpriteBox>> = {};

      function createSprite(landmarkKey: LandmarkYamlKey, displaying: 'on' | 'off') {
        const frames = spritesheet.animations[`${landmarkKey}_${displaying}`];
        let sprite: Sprite;
        if (enableTileAnimations) {
          const anim = sprite = new AnimatedSprite(frames);
          anim.animationSpeed = 1 / (500 * Ticker.targetFPMS);
          anim.play();
        }
        else {
          sprite = new Sprite(frames[0]);
        }

        sprite.position.set(LANDMARKS[landmarkKey].coords[0] - 8, LANDMARKS[landmarkKey].coords[1] - 8);
        container.addChild(sprite);
        return sprite;
      }

      for (const region of BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].allRegions) {
        if (!('loc' in region)) {
          continue;
        }

        spriteLookup[region.yamlKey] = {
          animated: enableTileAnimations,
          onSprite: createSprite(region.yamlKey, 'on'),
          offSprite: createSprite(region.yamlKey, 'off'),
        } as SpriteBox;
      }

      return { container, spriteLookup };
    },
  });

  effect(() => {
    const landmarks = landmarksResource.value();
    if (landmarks === null) {
      return;
    }

    const { spriteLookup } = landmarks;

    const checkedLocations = store.checkedLocations();
    const { allRegions } = store.defs();
    for (const [_, landmark] of strictObjectEntries(allRegions)) {
      if (!('loc' in landmark)) {
        continue;
      }

      const spriteBox = spriteLookup[landmark.yamlKey];
      if (!spriteBox) {
        continue;
      }

      if (checkedLocations.includes(landmark.loc)) {
        spriteBox.onSprite.visible = false;
        spriteBox.offSprite.visible = true;
      }
      else {
        spriteBox.onSprite.visible = true;
        spriteBox.offSprite.visible = false;
      }
    }
  });

  return landmarksResource.asReadonly();
}

interface SpriteBoxBase {
  animated: boolean;
  onSprite: Sprite;
  offSprite: Sprite;
}

interface NotAnimatedSpriteBox extends SpriteBoxBase {
  animated: false;
}

interface AnimatedSpriteBox extends SpriteBoxBase {
  animated: true;
  onSprite: AnimatedSprite;
  offSprite: AnimatedSprite;
}

export type SpriteBox = NotAnimatedSpriteBox | AnimatedSpriteBox;

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
    spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 },
  };

  // OnFrame2 (offsetX: 64)
  spritesheetData.frames[`${landmarkKey}_on_2`] = {
    frame: { x: 65, y: offsetY, w: 64, h: 64 },
    sourceSize: { w: 64, h: 64 },
    spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 },
  };

  // OffFrame1 (offsetX: 128)
  spritesheetData.frames[`${landmarkKey}_off_1`] = {
    frame: { x: 130, y: offsetY, w: 64, h: 64 },
    sourceSize: { w: 64, h: 64 },
    spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 },
  };

  // OffFrame2 (offsetX: 192)
  spritesheetData.frames[`${landmarkKey}_off_2`] = {
    frame: { x: 195, y: offsetY, w: 64, h: 64 },
    sourceSize: { w: 64, h: 64 },
    spriteSourceSize: { x: 0, y: 0, w: 64, h: 64 },
  };

  spritesheetData.animations[`${landmarkKey}_on`] = [`${landmarkKey}_on_1`, `${landmarkKey}_on_2`];
  spritesheetData.animations[`${landmarkKey}_off`] = [`${landmarkKey}_off_1`, `${landmarkKey}_off_2`];
}
