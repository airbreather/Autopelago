import { Component, DestroyRef, effect, ElementRef, inject, viewChild } from '@angular/core';

import { GameStore } from '../../../store/autopelago-store';
import { createFillerMarkers } from './filler-markers';
import { createLandmarkMarkers, createLandmarkSpritesheetResource } from './landmark-markers';
import { createPlayerToken, createPlayerTokenTextureResource } from './player-token';
import { Application, Ticker } from 'pixi.js';
import { resizeEvents } from '../../../util';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { GameDefinitionsStore } from '../../../store/game-definitions-store';

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
    const playerTokenTextureResource = createPlayerTokenTextureResource();
    const landmarkSpritesheetResource = createLandmarkSpritesheetResource();
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
