import { effect, resource, type Signal } from '@angular/core';
import type BitArray from '@bitarray/typedarray';
import type { Set as ImmutableSet } from 'immutable';
import { DropShadowFilter } from 'pixi-filters';
import { AnimatedSprite, Assets, Container, Sprite, Spritesheet, type SpritesheetData, Texture, Ticker } from 'pixi.js';
import { LANDMARKS, type LandmarkYamlKey } from '../../../../data/locations';
import {
  type AutopelagoDefinitions,
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  type VictoryLocationYamlKey,
} from '../../../../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';
import { strictObjectEntries } from '../../../../utils/types';

export interface CreateLandmarkMarkersOptions {
  game: Signal<AutopelagoClientAndData | null>;
  defs: Signal<AutopelagoDefinitions>;
  victoryLocationYamlKey: Signal<VictoryLocationYamlKey>;
  regionIsLandmarkWithUnsatisfiedRequirement: Signal<Readonly<BitArray>>;
  checkedLocations: Signal<ImmutableSet<number>>;
}

export function createLandmarkMarkers(options: CreateLandmarkMarkersOptions) {
  const landmarksResource = resource({
    defaultValue: null,
    params: () => options.game()?.connectScreenState ?? null,
    loader: async ({ params: connectScreenState }) => {
      if (connectScreenState === null) {
        return null;
      }

      const { enableTileAnimations } = connectScreenState;
      const spritesheetTexture = await Assets.load<Texture>('assets/images/locations.webp');
      const spritesheet = new Spritesheet(spritesheetTexture, spritesheetData);
      await spritesheet.parse();
      const victoryLocationYamlKey = options.victoryLocationYamlKey();
      const containers = {
        main: new Container({
          filters: [new DropShadowFilter({
            blur: 1,
            offset: { x: 11.2, y: 11.2 },
            color: 'black',
          })],
        }),
        quest: new Container({
          filters: [new DropShadowFilter({
            blur: 0.75,
            offset: { x: 8.4, y: 8.4 },
            color: 'black',
          })],
        }),
      } as const;
      const container = new Container({
        children: [containers.main, containers.quest],
      });

      // Create sprites for each landmark
      const spriteLookup: Partial<Record<LandmarkYamlKey, SpriteBox>> = {};
      function createSprite(landmarkKey: LandmarkYamlKey, displaying: 'on' | 'off', img: 'main' | 'quest') {
        const frames = spritesheet.animations[`${img === 'main' ? landmarkKey : 'q'}_${displaying}`];
        let sprite: Sprite;
        if (enableTileAnimations) {
          const anim = sprite = new AnimatedSprite(frames, true);
          anim.animationSpeed = 1 / (500 * Ticker.targetFPMS);
          anim.play();
        }
        else {
          sprite = new Sprite(frames[0]);
        }

        const [x, y] = LANDMARKS[landmarkKey].coords;
        sprite.position.set(x - (img === 'main' ? 8 : 6), y - (img === 'main' ? 8 : 21));
        if (img === 'quest') {
          sprite.scale = 0.75;
        }
        containers[img].addChild(sprite);
        return sprite;
      }

      for (const region of BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].allRegions) {
        if (!('loc' in region)) {
          continue;
        }

        spriteLookup[region.yamlKey] = {
          animated: connectScreenState.enableTileAnimations,
          onSprite: createSprite(region.yamlKey, 'on', 'main'),
          offSprite: createSprite(region.yamlKey, 'off', 'main'),
          onQSprite: createSprite(region.yamlKey, 'on', 'quest'),
          offQSprite: createSprite(region.yamlKey, 'off', 'quest'),
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

    const checkedLocations = options.checkedLocations();
    const regionIsLandmarkWithUnsatisfiedRequirement = options.regionIsLandmarkWithUnsatisfiedRequirement();
    const { allRegions } = options.defs();
    for (const [_, landmark] of strictObjectEntries(allRegions)) {
      if (!('loc' in landmark)) {
        continue;
      }

      const spriteBox = spriteLookup[landmark.yamlKey];
      if (!spriteBox) {
        continue;
      }

      if (checkedLocations.includes(landmark.loc)) {
        spriteBox.onSprite.visible = true;
        spriteBox.offSprite.visible = false;
        spriteBox.onQSprite.visible = false;
        spriteBox.offQSprite.visible = false;
      }
      else {
        spriteBox.onSprite.visible = false;
        spriteBox.offSprite.visible = true;
        if (regionIsLandmarkWithUnsatisfiedRequirement[landmark.key]) {
          spriteBox.onQSprite.visible = false;
          spriteBox.offQSprite.visible = true;
        }
        else {
          spriteBox.onQSprite.visible = true;
          spriteBox.offQSprite.visible = false;
        }
      }
    }
  });

  return landmarksResource.asReadonly();
}

interface SpriteBoxBase {
  animated: boolean;
  onSprite: Sprite;
  offSprite: Sprite;
  onQSprite: Sprite;
  offQSprite: Sprite;
}

interface NotAnimatedSpriteBox extends SpriteBoxBase {
  animated: false;
}

interface AnimatedSpriteBox extends SpriteBoxBase {
  animated: true;
  onSprite: AnimatedSprite;
  offSprite: AnimatedSprite;
  onQSprite: AnimatedSprite;
  offQSprite: AnimatedSprite;
}

export type SpriteBox = NotAnimatedSpriteBox | AnimatedSpriteBox;

// Create spritesheet data with frame definitions for each landmark
const spritesheetData: SpritesheetData & Required<Pick<SpritesheetData, 'animations'>> = {
  frames: {},
  meta: { scale: 4 },
  animations: {},
};

// Generate frame definitions for each landmark
for (const [landmarkKey, landmark] of [['q', { sprite_index: 0 }] as const, ...strictObjectEntries(LANDMARKS)]) {
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
