import { Component, DestroyRef, effect, ElementRef, inject, resource, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import {
  AnimatedSprite,
  Application,
  Assets,
  Container,
  Graphics,
  Sprite,
  Spritesheet,
  SpritesheetData,
  Texture,
  Ticker,
} from 'pixi.js';
import { DropShadowFilter } from 'pixi-filters';

import { FillerRegionName, fillerRegions, LANDMARKS } from '../../../data/locations';
import { GameStore } from '../../../store/autopelago-store';
import { GameDefinitionsStore } from '../../../store/game-definitions-store';
import { resizeEvents, strictObjectEntries } from '../../../util';

function createFillerMarkers(fillerCountsByRegion: Readonly<Partial<Record<FillerRegionName, number>>>) {
  const graphicsContainer = new Container({
    filters: [new DropShadowFilter({
      blur: 1,
      offset: { x: 2.4, y: 2.4 },
      color: 'black',
    })],
  });

  const gfx = new Graphics();
  for (const [_, r] of strictObjectEntries(fillerRegions(fillerCountsByRegion))) {
    for (const [x, y] of r.coords) {
      gfx.rect(x - 0.8, y - 0.8, 1.6, 1.6);
      gfx.fill('yellow');
    }
  }

  graphicsContainer.addChild(gfx);
  return graphicsContainer;
}

const ROTATION_SCALE = Math.PI / 3200;
const CYCLE = 1000;
const HALF_CYCLE = CYCLE / 2;
const QUARTER_CYCLE = CYCLE / 4;

function createPlayerToken(texture: Texture, destroyRef: DestroyRef) {
  const playerToken = new Sprite(texture);
  playerToken.position.set(2, 80);
  playerToken.scale.set(0.25);
  playerToken.filters = [new DropShadowFilter({
    blur: 1,
    offset: { x: 6, y: 6 },
    color: 'black',
  })];
  playerToken.anchor.set(0.5);

  const playerTokenContext = {
    playerToken,
    cycleTime: 0,
  };
  function doRotation(this: typeof playerTokenContext, t: Ticker) {
    this.cycleTime = (this.cycleTime + t.deltaMS) % CYCLE;
    this.playerToken.rotation = (Math.abs(this.cycleTime - HALF_CYCLE) - QUARTER_CYCLE) * ROTATION_SCALE;
  }

  Ticker.shared.add(doRotation, playerTokenContext);
  destroyRef.onDestroy(() => Ticker.shared.remove(doRotation, playerTokenContext));
  return playerToken;
}

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

function createLandmarkMarkers(spritesheet: Spritesheet) {
  const landmarksContainer = new Container({
    filters: [new DropShadowFilter({
      blur: 1,
      offset: { x: 11.2, y: 11.2 },
      color: 'black',
    })],
  });

  // Create sprites for each landmark
  landmarksContainer.addChild(...Object.entries(LANDMARKS).map(([landmarkKey, landmark]) => {
    const isOn = Math.random() < 0.5;
    const anim = new AnimatedSprite(spritesheet.animations[`${landmarkKey}_${isOn ? 'on' : 'off'}`]);
    anim.animationSpeed = 1 / (500 * Ticker.targetFPMS);
    anim.position.set(landmark.coords[0] - 8, landmark.coords[1] - 8);
    anim.play();
    return anim;
  }));

  return landmarksContainer;
}

@Component({
  selector: 'app-game-tab-map',
  template: `
    <div #outer class="outer">
      <!--suppress AngularNgOptimizedImage, HtmlUnknownTarget -->
      <img alt="map" src="assets/images/map.svg" />
      <canvas #pixiCanvas class="pixi-canvas" width="300" height="450">
      </canvas>
      <div #pauseButtonContainer class="pause-button-container" [style.margin-top]="'-' + pauseButtonContainer.clientHeight + 'px'">
        <button class="rat-toggle-button"
                [class.toggled-on]="paused()"
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
  readonly paused = this.#store.paused;

  protected readonly pixiCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('pixiCanvas');
  protected readonly outerDiv = viewChild.required<ElementRef<HTMLDivElement>>('outer');

  constructor() {
    const destroyRef = inject(DestroyRef);
    const fillerCountsByRegionSignal = inject(GameDefinitionsStore).fillerCountsByRegion;
    const playerTokenTextureResource = resource({
      loader: () => Assets.load<Texture>('assets/images/players/pack_rat.webp'),
    });
    const landmarkSpritesheetResource = resource({
      loader: async () => {
        const spritesheetTexture = await Assets.load<Texture>('assets/images/locations.webp');
        const spritesheet = new Spritesheet(spritesheetTexture, spritesheetData);
        await spritesheet.parse();
        return spritesheet;
      },
    });
    effect(() => {
      const canvas = this.pixiCanvas().nativeElement;
      const outerDiv = this.outerDiv().nativeElement;
      const fillerCountsByRegion = fillerCountsByRegionSignal();
      const playerTokenTexture = playerTokenTextureResource.value();
      const landmarkSpritesheet = landmarkSpritesheetResource.value();
      if (!(fillerCountsByRegion && playerTokenTexture && landmarkSpritesheet)) {
        return;
      }

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

        app.stage.addChild(createFillerMarkers(fillerCountsByRegion));
        app.stage.addChild(createLandmarkMarkers(landmarkSpritesheet));
        app.stage.addChild(createPlayerToken(playerTokenTexture, destroyRef));

        resizeEvents(outerDiv).pipe(
          // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
          takeUntilDestroyed(destroyRef),
        ).subscribe(({ target }) => {
          app.stage.scale.x = target.clientWidth * reciprocalOriginalWidth;
          app.stage.scale.y = target.clientHeight * reciprocalOriginalHeight;
          app.resize();
        });

        if (this.#store.paused()) {
          app.resize();
          app.render();
        }
        else {
          Ticker.shared.start();
        }
      })();
    });
  }

  togglePause() {
    this.#store.togglePause();
  }
}
