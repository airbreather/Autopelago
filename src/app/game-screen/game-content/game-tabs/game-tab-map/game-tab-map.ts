import { CdkConnectedOverlay, CdkOverlayOrigin } from '@angular/cdk/overlay';
import { NgOptimizedImage } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import BitArray from '@bitarray/typedarray';

import { Application, Ticker } from 'pixi.js';
import Queue from 'yocto-queue';
import { type Vec2 } from '../../../../data/locations';
import { VICTORY_LOCATION_CROP_LOOKUP } from '../../../../data/resolved-definitions';
import { GameStore } from '../../../../store/autopelago-store';
import { elementSizeSignal } from '../../../../utils/element-size';
import { createLivePixiObjects } from './live-pixi-objects';
import { Tooltip } from './tooltip/tooltip';

const TOOLTIP_DELAY = 300;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-game-tab-map',
  imports: [
    CdkConnectedOverlay,
    CdkOverlayOrigin,
    NgOptimizedImage,
    Tooltip,
  ],
  template: `
    <div #outer class="outer" [style.--ap-throttled-scale]="throttledScaleForInvisibleElementsOnly()">
      <img class="map-img" alt="map" [ngSrc]="mapUrl()" width="300" height="450"/>
      <canvas #pixiCanvas class="pixi-canvas" width="300" height="450">
      </canvas>
      @for (lm of allLandmarks(); track $index) {
        <div #hoverBox class="hover-box" [tabindex]="$index + 999" cdkOverlayOrigin
             [style.--ap-left-base.px]="lm[1][0]" [style.--ap-top-base.px]="lm[1][1]"
             (focus)="onFocusTooltip(lm[0], hoverBox)" (mouseenter)="onFocusTooltip(lm[0], hoverBox)"
             (blur)="onBlurTooltip()" (mouseleave)="onBlurTooltip()">
        </div>
      }
      <div #pauseButtonContainer class="pause-button-container"
           [style.margin-top]="'-' + pauseButtonContainer.clientHeight + 'px'">
        <button class="rat-toggle-button"
                [class.toggled-on]="!running()"
                (click)="togglePause()">
          ⏸
        </button>
      </div>
    </div>
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="tooltipTarget()?.[1] ?? outer"
      [cdkConnectedOverlayOpen]="tooltipTarget() !== null"
      [cdkConnectedOverlayUsePopover]="'inline'"
      (detach)="detachTooltip()">
      <app-tooltip [landmarkKey]="tooltipTarget()![0]">
      </app-tooltip>
    </ng-template>
  `,
  styles: `
    .outer {
      position: relative;
      pointer-events: none;
      user-select: none;
    }

    .map-img {
      width: 100%;
      height: 100%;
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

    .hover-box {
      position: absolute;
      pointer-events: initial;
      width: calc(16px * var(--ap-throttled-scale, 1));
      height: calc(16px * var(--ap-throttled-scale, 1));
      left: calc((var(--ap-left-base, 8px) - 8px) * var(--ap-throttled-scale, 1));
      top: calc((var(--ap-top-base, 8px) - 8px) * var(--ap-throttled-scale, 1));
    }
  `,
})
export class GameTabMap {
  readonly #store = inject(GameStore);
  protected readonly running = this.#store.running;
  protected readonly throttledScaleForInvisibleElementsOnly = signal(1);

  readonly allLandmarks = computed(() => {
    const { allLocations, allRegions, startRegion } = this.#store.defs();
    const result: [number, Vec2][] = [];
    const visited = new BitArray(allRegions.length);
    const q = new Queue<number>();
    function tryEnqueue(r: number) {
      if (!visited[r]) {
        visited[r] = 1;
        q.enqueue(r);
      }
    }
    q.enqueue(startRegion);
    for (let r = q.dequeue(); r !== undefined; r = q.dequeue()) {
      const region = allRegions[r];
      if ('loc' in region) {
        result.push([r, allLocations[region.loc].coords]);
      }
      for (const [nxt] of region.connected.all) {
        tryEnqueue(nxt);
      }
    }
    return result;
  });

  readonly #tooltipTarget = signal<[number, HTMLDivElement] | null>(null);
  readonly tooltipTarget = this.#tooltipTarget.asReadonly();

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
        return '';
    }
  });

  constructor() {
    const app = new Application();
    inject(DestroyRef).onDestroy(() => {
      app.ticker.stop();
      app.destroy();
      appIsInitialized.set(false);
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
    const {
      playerTokenResource,
      landmarksResource,
      fillerMarkersSignal,
    } = createLivePixiObjects(this.#store, app.ticker);

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
      if (!(playerToken && landmarks)) {
        return;
      }

      const fillerMarkers = fillerMarkersSignal();
      app.stage.addChild(fillerMarkers.container);
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
    // throttle this signal update, though, since it triggers a full re-render.
    let setThrottledScaleTimeout: number | null = null;
    let setThrottledScaleValue = NaN;
    effect(() => {
      if (!appIsInitialized()) {
        return;
      }

      const victoryLocationYamlKey = this.#store.victoryLocationYamlKey();
      const { clientHeight } = outerDivSize();
      const scale = clientHeight / VICTORY_LOCATION_CROP_LOOKUP[victoryLocationYamlKey];
      app.stage.scale = scale;
      app.resize();

      // if there's a tooltip active, then turn it off.
      const tooltipTarget = untracked(() => this.#tooltipTarget());
      if (tooltipTarget !== null) {
        tooltipTarget[1].blur();
        this.#tooltipTarget.set(null);
      }
      setThrottledScaleValue = scale;
      setThrottledScaleTimeout ??= setTimeout(() => {
        this.throttledScaleForInvisibleElementsOnly.set(setThrottledScaleValue);
        setThrottledScaleValue = NaN;
        setThrottledScaleTimeout = null;
      }, 200);
    });
  }

  #prevFocusTimeout = NaN;
  #prevBlurTimeout = NaN;
  onFocusTooltip(landmark: number, element: HTMLDivElement) {
    if (!Number.isNaN(this.#prevBlurTimeout)) {
      clearTimeout(this.#prevBlurTimeout);
      this.#prevBlurTimeout = NaN;
      this.#tooltipTarget.set([landmark, element]);
      return;
    }

    if (!Number.isNaN(this.#prevFocusTimeout)) {
      clearTimeout(this.#prevFocusTimeout);
      this.#prevFocusTimeout = NaN;
    }
    this.#prevFocusTimeout = setTimeout(() => {
      this.#tooltipTarget.set([landmark, element]);
      this.#prevFocusTimeout = NaN;
    }, TOOLTIP_DELAY);
  }

  onBlurTooltip() {
    if (!Number.isNaN(this.#prevFocusTimeout)) {
      clearTimeout(this.#prevFocusTimeout);
      this.#prevFocusTimeout = NaN;
      this.#tooltipTarget.set(null);
      return;
    }

    if (!Number.isNaN(this.#prevBlurTimeout)) {
      clearTimeout(this.#prevBlurTimeout);
      this.#prevBlurTimeout = NaN;
    }
    this.#prevBlurTimeout = setTimeout(() => {
      this.#tooltipTarget.set(null);
      this.#prevBlurTimeout = NaN;
    }, TOOLTIP_DELAY);
  }

  togglePause() {
    this.#store.togglePause();
  }

  detachTooltip() {
    if (!Number.isNaN(this.#prevFocusTimeout)) {
      clearTimeout(this.#prevFocusTimeout);
      this.#prevFocusTimeout = NaN;
    }

    if (!Number.isNaN(this.#prevBlurTimeout)) {
      clearTimeout(this.#prevBlurTimeout);
      this.#prevBlurTimeout = NaN;
    }

    this.#tooltipTarget.set(null);
  }
}
