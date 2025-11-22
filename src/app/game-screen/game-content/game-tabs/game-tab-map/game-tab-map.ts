import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  resource,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DropShadowFilter } from 'pixi-filters';

import {
  AnimatedSprite,
  Application,
  Assets,
  Container,
  Graphics,
  Sprite,
  Spritesheet,
  type SpritesheetData,
  Texture,
  Ticker,
} from 'pixi.js';

import {
  type Filler,
  fillerRegionCoords,
  type FillerRegionYamlKey,
  isFillerRegionYamlKey,
  LANDMARKS,
  type LandmarkYamlKey,
} from '../../../../data/locations';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  VICTORY_LOCATION_CROP_LOOKUP,
  type VictoryLocationYamlKey,
} from '../../../../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';
import { resizeEvents } from '../../../../element-size';
import { GameStore } from '../../../../store/autopelago-store';
import { strictObjectEntries } from '../../../../util';
import { createPlayerToken } from './player-token';

const fillerCoordsByRegionLookup = {
  captured_goldfish: getFillerCoordsByRegion('captured_goldfish'),
  secret_cache: getFillerCoordsByRegion('secret_cache'),
  snakes_on_a_planet: getFillerCoordsByRegion('snakes_on_a_planet'),
} as const satisfies Record<VictoryLocationYamlKey, Partial<Record<FillerRegionYamlKey, Filler>>>;

function getFillerCoordsByRegion(victoryLocation: VictoryLocationYamlKey) {
  const fillerCountsByRegion: Partial<Record<FillerRegionYamlKey, number>> = {};
  for (const r of BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocation].allRegions) {
    if (isFillerRegionYamlKey(r.yamlKey) && 'locs' in r) {
      fillerCountsByRegion[r.yamlKey] = r.locs.length;
    }
  }

  return fillerRegionCoords(fillerCountsByRegion);
}

function createFillerMarkers(fillerCoordsByRegion: Readonly<Partial<Record<FillerRegionYamlKey, Filler>>>) {
  const graphicsContainer = new Container({
    filters: [new DropShadowFilter({
      blur: 1,
      offset: { x: 2.4, y: 2.4 },
      color: 'black',
    })],
  });

  const gfx = new Graphics();
  for (const [_, r] of strictObjectEntries(fillerCoordsByRegion)) {
    for (const [x, y] of r.coords) {
      gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
      gfx.fill('yellow');
    }
  }

  graphicsContainer.addChild(gfx);
  return graphicsContainer;
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

type SpriteBox = NotAnimatedSpriteBox | AnimatedSpriteBox;

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

function createLandmarkMarkers(victoryLocationYamlKey: VictoryLocationYamlKey, enableTileAnimations: boolean, spritesheet: Spritesheet) {
  const landmarksContainer = new Container({
    filters: [new DropShadowFilter({
      blur: 1,
      offset: { x: 11.2, y: 11.2 },
      color: 'black',
    })],
  });

  // Create sprites for each landmark
  const landmarkSpriteLookup: Partial<Record<LandmarkYamlKey, SpriteBox>> = { };
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
    landmarksContainer.addChild(sprite);
    return sprite;
  }

  for (const region of BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].allRegions) {
    if (!('loc' in region)) {
      continue;
    }

    landmarkSpriteLookup[region.yamlKey] = {
      animated: enableTileAnimations,
      onSprite: createSprite(region.yamlKey, 'on'),
      offSprite: createSprite(region.yamlKey, 'off'),
    } as SpriteBox;
  }

  return { landmarksContainer, landmarkSpriteLookup };
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-game-tab-map',
  template: `
    <div #outer class="outer">
      <!--suppress AngularNgOptimizedImage, HtmlUnknownTarget -->
      <img alt="map" [src]="mapUrl()" />
      <canvas #pixiCanvas class="pixi-canvas" width="300" height="450">
      </canvas>
      <div #pauseButtonContainer class="pause-button-container"
           [style.margin-top]="'-' + pauseButtonContainer.clientHeight + 'px'">
        <button class="rat-toggle-button"
                [class.toggled-on]="!running()"
                (click)="togglePause()">
          ⏸
        </button>
      </div>
    </div>
  `,
  styles: `
    .outer {
      position: relative;
      pointer-events: none;
      user-select: none;
    }

    .pause-button-container {
      position: sticky;
      margin-bottom: 0;
      left: 5px;
      bottom: 5px;
      pointer-events: initial;
      width: fit-content;
    }

    .pixi-canvas {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
    }
  `,
})
export class GameTabMap {
  readonly #store = inject(GameStore);
  protected readonly running = this.#store.running;

  readonly game = input.required<AutopelagoClientAndData>();

  protected readonly pixiCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('pixiCanvas');
  protected readonly outerDiv = viewChild.required<ElementRef<HTMLDivElement>>('outer');

  protected readonly mapUrl = computed(() => {
    switch (this.#store.victoryLocationYamlKey()) {
      case 'captured_goldfish':
        return 'assets/images/map-min.svg';
      case 'secret_cache':
        return 'assets/images/map-med.svg';
      case 'snakes_on_a_planet':
        return 'assets/images/map.svg';
      default:
        return null;
    }
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    const playerTokenResource = createPlayerToken({
      store: this.#store,
      ticker: Ticker.shared,
      enableRatAnimationsSignal: computed(() => this.game().connectScreenState.enableRatAnimations),
    });
    const landmarkSpritesheetResource = resource({
      loader: async () => {
        const spritesheetTexture = await Assets.load<Texture>('assets/images/locations.webp');
        const spritesheet = new Spritesheet(spritesheetTexture, spritesheetData);
        await spritesheet.parse();
        return spritesheet;
      },
    });
    const landmarkMarkersContainer = signal<Container | null>(null);
    const landmarkSpriteLookup = signal<Partial<Record<LandmarkYamlKey, SpriteBox>> | null>(null);
    const fillerMarkersContainer = signal<Container | null>(null);
    effect(() => {
      const fillerMarkers = fillerMarkersContainer();
      if (!fillerMarkers) {
        return;
      }

      const victoryLocationYamlKey = this.#store.victoryLocationYamlKey();
      const checkedLocations = this.#store.checkedLocations();
      const defs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
      const coordsByRegion = fillerCoordsByRegionLookup[victoryLocationYamlKey];
      const gfx = new Graphics();
      for (const region of defs.allRegions) {
        if (!('locs' in region)) {
          continue;
        }

        if (!(region.yamlKey in coordsByRegion)) {
          continue;
        }

        const fillerCoords = coordsByRegion[region.yamlKey];
        if (!fillerCoords) {
          continue;
        }

        for (const [i, [x, y]] of fillerCoords.coords.entries()) {
          gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
          gfx.fill(checkedLocations.includes(region.locs[i]) ? 'grey' : 'yellow');
        }
      }

      fillerMarkers.replaceChild(fillerMarkers.children[0], gfx);
    });
    effect(() => {
      const canvas = this.pixiCanvas().nativeElement;
      const outerDiv = this.outerDiv().nativeElement;
      const { enableTileAnimations } = this.game().connectScreenState;
      const playerToken = playerTokenResource.value();
      const landmarkSpritesheet = landmarkSpritesheetResource.value();
      if (!(playerToken && landmarkSpritesheet)) {
        return;
      }

      const victoryLocationYamlKey = this.#store.victoryLocationYamlKey();
      canvas.height = VICTORY_LOCATION_CROP_LOOKUP[victoryLocationYamlKey];
      const reciprocalOriginalWidth = 1 / canvas.width;
      const reciprocalOriginalHeight = 1 / canvas.height;
      void (async () => {
        const app = new Application();
        await app.init({
          canvas,
          resizeTo: outerDiv,
          backgroundAlpha: 0,
          antialias: false,
          sharedTicker: true,
          autoStart: false,
        });
        Ticker.shared.stop();

        const fillerMarkers = createFillerMarkers(fillerCoordsByRegionLookup[victoryLocationYamlKey]);
        fillerMarkersContainer.set(fillerMarkers);
        app.stage.addChild(fillerMarkers);
        Ticker.shared.stop();
        const landmarkMarkers = createLandmarkMarkers(victoryLocationYamlKey, enableTileAnimations, landmarkSpritesheet);
        landmarkMarkersContainer.set(landmarkMarkers.landmarksContainer);
        landmarkSpriteLookup.set(landmarkMarkers.landmarkSpriteLookup);
        app.stage.addChild(landmarkMarkers.landmarksContainer);
        Ticker.shared.stop();
        app.stage.addChild(playerToken);
        Ticker.shared.stop();

        resizeEvents(outerDiv).pipe(
          // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
          takeUntilDestroyed(destroyRef),
        ).subscribe(({ target }) => {
          app.stage.scale.x = target.clientWidth * reciprocalOriginalWidth;
          app.stage.scale.y = target.clientHeight * reciprocalOriginalHeight;
          app.resize();
        });

        if (untracked(() => this.#store.running())) {
          Ticker.shared.start();
        }
        else {
          app.resize();
          app.render();
        }
      })();
    });

    effect(() => {
      const landmarkMarkersContainerNotNull = landmarkMarkersContainer();
      const landmarkSpriteLookupNotNull = landmarkSpriteLookup();
      if (!(landmarkMarkersContainerNotNull && landmarkSpriteLookupNotNull)) {
        return;
      }

      const checkedLocations = this.#store.checkedLocations();
      const defs = this.#store.defs();
      for (const [_, landmark] of strictObjectEntries(defs.allRegions)) {
        if (!('loc' in landmark)) {
          continue;
        }

        const spriteBox = landmarkSpriteLookupNotNull[landmark.yamlKey];
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

    effect(() => {
      if (this.#store.running()) {
        Ticker.shared.start();
      }
      else {
        Ticker.shared.stop();
      }
    });
  }

  togglePause() {
    this.#store.togglePause();
  }
}
