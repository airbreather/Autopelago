import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';

import { Application, Ticker } from 'pixi.js';
import { VICTORY_LOCATION_CROP_LOOKUP } from '../../../../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';
import { GameStore } from '../../../../store/autopelago-store';
import { elementSizeSignal } from '../../../../utils/element-size';
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
    inject(DestroyRef).onDestroy(() => {
      app.destroy();
    });
    app.ticker = new Ticker();
    effect(() => {
      if (this.#store.running()) {
        app.ticker.start();
      }
      else {
        app.ticker.stop();
      }
    });

    // these resources (and signal) need to be created in our injection context, and they have their
    // own asynchronous initialization (though the signal is only pseudo-asynchronous, since it gets
    // initialized during the first microtask tick after we're done).
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

    // app.init is async, and nothing can be done with the app until it's initialized, so let's do
    // just everything up to app.init and then open up the floodgates for everything afterward by
    // giving them the initialized app.
    const appIsInitialized = signal(false);
    effect(() => {
      const canvas = this.pixiCanvas().nativeElement;
      const outerDiv = this.outerDiv().nativeElement;

      const victoryLocationYamlKey = this.#store.victoryLocationYamlKey();
      canvas.height = VICTORY_LOCATION_CROP_LOOKUP[victoryLocationYamlKey];
      void app.init({
        canvas,
        resizeTo: outerDiv,
        backgroundAlpha: 0,
        antialias: false,
        autoStart: false,
      }).then(() => {
        appIsInitialized.set(true);
      });
    });

    // add stuff to the app stage, but only once it's initialized. for the sake of simplicity, DON'T
    // observe any signals in this effect that will change after everything has been initialized. I
    // really don't want to bother removing and recreating stuff for no reason.
    effect(() => {
      if (!appIsInitialized()) {
        return;
      }

      const playerToken = playerTokenResource.value();
      const landmarks = landmarksResource.value();
      const fillerMarkers = fillerMarkersSignal();
      if (!(playerToken && landmarks && fillerMarkers)) {
        return;
      }

      app.stage.addChild(fillerMarkers);
      app.stage.addChild(landmarks.container);
      app.stage.addChild(playerToken);
      app.resize();
      app.render();
      if (untracked(() => this.#store.running())) {
        app.ticker.start();
      }
      else {
        app.ticker.stop();
      }
    });

    // whenever the outer div resizes, we also need to resize the app to match.
    const outerDivSize = elementSizeSignal(this.outerDiv);
    effect(() => {
      if (!appIsInitialized()) {
        return;
      }

      const victoryLocationYamlKey = this.#store.victoryLocationYamlKey();
      const { clientWidth, clientHeight } = outerDivSize();
      app.stage.scale.x = clientWidth / 300;
      app.stage.scale.y = clientHeight / VICTORY_LOCATION_CROP_LOOKUP[victoryLocationYamlKey];
      app.resize();
    });
  }

  togglePause() {
    this.#store.togglePause();
  }
}
