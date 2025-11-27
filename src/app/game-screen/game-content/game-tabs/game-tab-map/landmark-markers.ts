import { DestroyRef, effect, inject, type Resource, resource, type Signal } from '@angular/core';
import type BitArray from '@bitarray/typedarray';
import { DropShadowFilter } from 'pixi-filters';
import { AnimatedSprite, Assets, Container, Sprite, Spritesheet, type SpritesheetData, Texture, Ticker } from 'pixi.js';
import { isLandmarkYamlKey, LANDMARKS, type LandmarkYamlKey } from '../../../../data/locations';
import {
  type AutopelagoDefinitions,
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  type VictoryLocationYamlKey,
} from '../../../../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';
import { strictObjectEntries } from '../../../../utils/types';

export interface CreateLandmarkMarkersOptions {
  ticker: Ticker;
  game: Signal<AutopelagoClientAndData | null>;
  defs: Signal<AutopelagoDefinitions>;
  victoryLocationYamlKey: Signal<VictoryLocationYamlKey>;
  regionIsLandmarkWithRequirementSatisfied: Signal<Readonly<BitArray>>;
}

type FullSpriteLookupType = {
  [T in LandmarkYamlKey]: T extends 'moon_comma_the' ? MoonSpriteBox : LandmarkSpriteBox;
};

export interface LandmarkMarkers {
  container: Container;
  spriteLookup: Partial<FullSpriteLookupType>;
}

export function createLandmarkMarkers(options: CreateLandmarkMarkersOptions): Resource<LandmarkMarkers | null> {
  const destroyRef = inject(DestroyRef);
  function updateAnimatedSprite(this: AnimatedSprite, t: Ticker) {
    this.update(t);
  }
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
      const spriteLookup: Partial<FullSpriteLookupType> = {};
      function createSprite(landmarkKey: keyof FullSpriteLookupType, displaying: 'on' | 'off', img: 'main' | 'quest') {
        const frames = spritesheet.animations[`${img === 'main' ? landmarkKey : 'q'}_${displaying}`];
        let sprite: Sprite;
        if (enableTileAnimations) {
          const anim = sprite = new AnimatedSprite(frames, false);
          anim.animationSpeed = 1 / (500 * Ticker.targetFPMS);
          options.ticker.add(updateAnimatedSprite, anim);
          destroyRef.onDestroy(() => options.ticker.remove(updateAnimatedSprite, anim));
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
        const yamlKey = region.yamlKey;
        if (!isLandmarkYamlKey(yamlKey)) {
          continue;
        }

        if (yamlKey === 'moon_comma_the') {
          const spriteBox = spriteLookup[yamlKey] = {
            animated: connectScreenState.enableTileAnimations,
            onSprite: createSprite(yamlKey, 'on', 'main'),
          } as MoonSpriteBox;
          spriteBox.onSprite.visible = false;
        }
        else {
          const spriteBox = spriteLookup[yamlKey] = {
            animated: connectScreenState.enableTileAnimations,
            onSprite: createSprite(yamlKey, 'on', 'main'),
            offSprite: createSprite(yamlKey, 'off', 'main'),
            onQSprite: createSprite(yamlKey, 'on', 'quest'),
            offQSprite: createSprite(yamlKey, 'off', 'quest'),
          } as LandmarkSpriteBox;
          spriteBox.onSprite.visible = false;
          spriteBox.offSprite.visible = true;
          spriteBox.onQSprite.visible = false;
          spriteBox.offQSprite.visible = true;
        }
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

    const { allRegions } = options.defs();
    const regionIsLandmarkWithRequirementSatisfied = options.regionIsLandmarkWithRequirementSatisfied();
    for (const landmark of allRegions) {
      const yamlKey = landmark.yamlKey;
      if (!isLandmarkYamlKey(yamlKey) || yamlKey === 'moon_comma_the') {
        continue;
      }

      const spriteBox = spriteLookup[yamlKey];
      if (!spriteBox) {
        continue;
      }

      if (!(spriteBox.onQSprite.visible || spriteBox.offQSprite.visible)) {
        continue;
      }

      if (regionIsLandmarkWithRequirementSatisfied[landmark.key]) {
        spriteBox.onQSprite.visible = true;
        spriteBox.offQSprite.visible = false;
      }
      else {
        spriteBox.onQSprite.visible = false;
        spriteBox.offQSprite.visible = true;
      }
    }
  });

  return landmarksResource.asReadonly();
}

interface SpriteBox_<TAnimated extends boolean> {
  animated: TAnimated;
  onSprite: (TAnimated extends true ? AnimatedSprite : Sprite);
}

interface LandmarkSpriteBox_<TAnimated extends boolean> extends SpriteBox_<TAnimated> {
  offSprite: (TAnimated extends true ? AnimatedSprite : Sprite);
  onQSprite: (TAnimated extends true ? AnimatedSprite : Sprite);
  offQSprite: (TAnimated extends true ? AnimatedSprite : Sprite);
}

export type LandmarkSpriteBox =
  | LandmarkSpriteBox_<true>
  | LandmarkSpriteBox_<false>
  ;

export type MoonSpriteBox =
  | SpriteBox_<true>
  | SpriteBox_<false>
  ;

export type SpriteBox =
  | LandmarkSpriteBox
  | MoonSpriteBox
  ;

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
