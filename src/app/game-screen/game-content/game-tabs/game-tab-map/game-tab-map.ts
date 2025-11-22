import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Application, Ticker } from 'pixi.js';
import { VICTORY_LOCATION_CROP_LOOKUP } from '../../../../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';
import { resizeEvents } from '../../../../element-size';
import { GameStore } from '../../../../store/autopelago-store';
import { createFillerMarkers } from './filler-markers';
import { createLandmarkMarkers } from './landmark-markers';
import { createPlayerToken } from './player-token';

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
    const app = new Application();
    app.ticker = new Ticker();
    const destroyRef = inject(DestroyRef);
    const playerTokenResource = createPlayerToken({
      store: this.#store,
      ticker: app.ticker,
      enableRatAnimationsSignal: computed(() => this.game().connectScreenState.enableRatAnimations),
    });
    const landmarksResource = createLandmarkMarkers({
      store: this.#store,
      enableTileAnimationsSignal: computed(() => this.game().connectScreenState.enableTileAnimations),
    });
    const fillerMarkersSignal = createFillerMarkers({
      store: this.#store,
    });

    effect(() => {
      if (this.#store.running()) {
        app.ticker.start();
      }
      else {
        app.ticker.stop();
      }
    });

    effect(() => {
      const canvas = this.pixiCanvas().nativeElement;
      const outerDiv = this.outerDiv().nativeElement;
      const playerToken = playerTokenResource.value();
      const landmarks = landmarksResource.value();
      const fillerMarkers = fillerMarkersSignal();
      if (!(playerToken && landmarks && fillerMarkers)) {
        return;
      }

      const victoryLocationYamlKey = this.#store.victoryLocationYamlKey();
      canvas.height = VICTORY_LOCATION_CROP_LOOKUP[victoryLocationYamlKey];
      const reciprocalOriginalWidth = 1 / canvas.width;
      const reciprocalOriginalHeight = 1 / canvas.height;
      void (async () => {
        await app.init({
          canvas,
          resizeTo: outerDiv,
          backgroundAlpha: 0,
          antialias: false,
          autoStart: false,
        });

        app.stage.addChild(fillerMarkers);
        app.ticker.stop();
        app.stage.addChild(landmarks.container);
        app.ticker.stop();
        app.stage.addChild(playerToken);
        app.ticker.stop();

        resizeEvents(outerDiv).pipe(
          // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
          takeUntilDestroyed(destroyRef),
        ).subscribe(({ target }) => {
          app.stage.scale.x = target.clientWidth * reciprocalOriginalWidth;
          app.stage.scale.y = target.clientHeight * reciprocalOriginalHeight;
          app.resize();
        });

        if (untracked(() => this.#store.running())) {
          app.ticker.start();
        }
        else {
          app.resize();
          app.render();
        }
      })();
    });
  }

  togglePause() {
    this.#store.togglePause();
  }
}
