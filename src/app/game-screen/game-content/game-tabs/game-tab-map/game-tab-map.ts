import { CdkConnectedOverlay } from '@angular/cdk/overlay';
import { NgOptimizedImage } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
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
import { createEmptyTooltipContext, TooltipBehavior, type TooltipOriginProps } from '../../../../tooltip-behavior';
import { elementSizeSignal } from '../../../../utils/element-size';
import { LandmarkTooltip } from './landmark-tooltip';
import { createLivePixiObjects } from './live-pixi-objects';
import { PlayerTooltip } from './player-tooltip';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-game-tab-map',
  imports: [
    CdkConnectedOverlay,
    NgOptimizedImage,
    LandmarkTooltip,
    PlayerTooltip,
    TooltipBehavior,
  ],
  template: `
    <div #outer class="outer">
      <img class="map-img" alt="map" [ngSrc]="mapUrl()" width="300" height="450" priority />
      <canvas #pixiCanvas class="pixi-canvas" width="300" height="450">
      </canvas>
      @for (lm of allLandmarks(); track $index) {
        <div #hoverBox class="hover-box" [tabindex]="$index + 999"
             [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]"
             appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(lm.landmark, $event, true)">
        </div>
      }
      <div #playerTokenHoverBox class="hover-box" [tabIndex]="998" [style.z-index]="999"
           appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(null, $event, true)">
      </div>
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
      [cdkConnectedOverlayOrigin]="tooltipOrigin()?.element ?? outer"
      [cdkConnectedOverlayOpen]="tooltipOrigin() !== null"
      [cdkConnectedOverlayUsePopover]="'inline'"
      (detach)="tooltipOrigin()?.notifyDetached()">
      @if (tooltipOrigin(); as origin) {
        @if (origin.landmark; as landmark) {
          <app-landmark-tooltip [landmarkKey]="landmark" />
        }
        @else {
          <app-player-tooltip />
        }
      }
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
      width: calc(16px * var(--ap-scale, 1));
      height: calc(16px * var(--ap-scale, 1));
      left: calc((var(--ap-left-base, 8px) - 8px) * var(--ap-scale, 1));
      top: calc((var(--ap-top-base, 8px) - 8px) * var(--ap-scale, 1));
    }
  `,
})
export class GameTabMap {
  readonly #store = inject(GameStore);
  protected readonly running = this.#store.running;

  readonly allLandmarks = computed(() => {
    const { allLocations, allRegions, startRegion } = this.#store.defs();
    const result: LandmarkProps[] = [];
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
        result.push({ landmark: r, coords: allLocations[region.loc].coords });
      }
      for (const [nxt] of region.connected.all) {
        tryEnqueue(nxt);
      }
    }
    return result;
  });

  // all tooltips here should use the same context, so that the user can quickly switch between them
  // without having to sit through the whole delay.
  protected readonly tooltipContext = createEmptyTooltipContext();
  readonly #tooltipOrigin = signal<CurrentTooltipOriginProps | null>(null);
  protected readonly tooltipOrigin = this.#tooltipOrigin.asReadonly();

  protected readonly pixiCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('pixiCanvas');
  protected readonly outerDiv = viewChild.required<ElementRef<HTMLDivElement>>('outer');
  protected readonly playerTokenHoverBox = viewChild.required<ElementRef<HTMLDivElement>>('playerTokenHoverBox');

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
      app.destroy(
        {
          removeView: true,
          releaseGlobalResources: true,
        },
        {
          children: true,
          style: true,
          context: true,
          // these are cached, which PixiJS warns about if we go out and in again:
          textureSource: false,
          texture: false,
        });
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
      app.stage.addChild(playerToken.sprite);
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
      const { clientHeight } = outerDivSize();
      const scale = clientHeight / VICTORY_LOCATION_CROP_LOOKUP[victoryLocationYamlKey];
      app.stage.scale = scale;
      app.resize();
      this.#tooltipOrigin.update((tooltipOrigin) => {
        if (tooltipOrigin !== null) {
          tooltipOrigin.notifyDetached();
        }
        return null;
      });
      this.outerDiv().nativeElement.style.setProperty('--ap-scale', scale.toString());
    });

    const injector = inject(Injector);
    const setupRatHoverBoxEffect = effect(() => {
      if (!appIsInitialized()) {
        return;
      }

      const playerToken = playerTokenResource.value();
      if (playerToken === null) {
        return;
      }

      const playerTokenHoverBox = this.playerTokenHoverBox().nativeElement;
      const playerPosition = playerToken.position;
      setTimeout(() => {
        effect(() => {
          const [x, y] = playerPosition();
          playerTokenHoverBox.style.setProperty('--ap-left-base', `${x.toString()}px`);
          playerTokenHoverBox.style.setProperty('--ap-top-base', `${y.toString()}px`);
        }, { injector });
      });
      setupRatHoverBoxEffect.destroy();
    });
  }

  togglePause() {
    this.#store.togglePause();
  }

  setTooltipOrigin(landmark: number | null, props: TooltipOriginProps | null, fromDirective: boolean) {
    this.#tooltipOrigin.update((prev) => {
      if (prev !== null && !fromDirective) {
        prev.notifyDetached();
      }
      return props === null
        ? null
        : { landmark, ...props };
    });
  }
}

interface LandmarkProps {
  landmark: number;
  coords: Vec2;
}

interface CurrentTooltipOriginProps {
  landmark: number | null;
  element: HTMLElement;
  notifyDetached: () => void;
}
