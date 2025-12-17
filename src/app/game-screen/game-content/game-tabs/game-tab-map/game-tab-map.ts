import {
  CdkConnectedOverlay,
  createFlexibleConnectedPositionStrategy,
  createRepositionScrollStrategy,
} from '@angular/cdk/overlay';
import { NgOptimizedImage } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  resource,
  runInInjectionContext,
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import BitArray from '@bitarray/typedarray';

import Queue from 'yocto-queue';
import { LANDMARKS, type LandmarkYamlKey, type Vec2 } from '../../../../data/locations';
import { VICTORY_LOCATION_CROP_LOOKUP } from '../../../../data/resolved-definitions';
import { GameStore } from '../../../../store/autopelago-store';
import { GameScreenStore } from '../../../../store/game-screen-store';
import { createEmptyTooltipContext, TooltipBehavior, type TooltipOriginProps } from '../../../../tooltip-behavior';
import { elementSizeSignal } from '../../../../utils/element-size';
import { PerformanceInsensitiveAnimatableState } from '../../status-display/performance-insensitive-animatable-state';
import { LocationTooltip } from './location-tooltip';
import { PlayerTooltip } from './player-tooltip';
import { watchAnimations } from './watch-animations';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-game-tab-map',
  imports: [
    CdkConnectedOverlay,
    NgOptimizedImage,
    LocationTooltip,
    PlayerTooltip,
    TooltipBehavior,
  ],
  template: `
    <div #outer class="outer">
      <img class="map-img" alt="map" [ngSrc]="mapUrl()" width="300" height="450" priority />
      <div class="organize fillers">
        @for (f of allFillers(); track f.loc) {
          <div
            #fillerSquare class="hover-box filler" [tabindex]="$index + 1999"
            [style.--ap-left-base.px]="f.coords[0]" [style.--ap-top-base.px]="f.coords[1]"
            [attr.data-location-id]="f.loc"
            appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(f.loc, $event, true)"
            (click)="hyperFocus(f.loc)" (keyup.enter)="hyperFocus(f.loc)" (keyup.space)="hyperFocus(f.loc)">
          </div>
        }
      </div>
      <svg class="dashed-path" viewBox="0 0 300 450" preserveAspectRatio="none">
        <path #dashedPath fill="none" stroke="red" stroke-width="1" stroke-dasharray="3 1" d="">
          <animate
            id="animate-stroke-dashoffset"
            attributeName="stroke-dashoffset"
            values="4;0"
            dur="1s"
            repeatCount="indefinite" />
        </path>
      </svg>
      <div class="organize landmarks">
        @for (lm of allLandmarks(); track lm.loc) {
          <div
            #landmarkContainer class="hover-box landmark" [tabindex]="$index + 999"
            [attr.data-location-id]="lm.loc" [style.--ap-checked-offset]="lm.yamlKey === 'moon_comma_the' ? 0 : 'unset'"
            [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]"
            appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(lm.loc, $event, true)"
            (click)="hyperFocus(lm.loc)" (keyup.enter)="hyperFocus(lm.loc)" (keyup.space)="hyperFocus(lm.loc)">
            <!--suppress CheckImageSize -->
            <img width="64" height="64" [alt]="lm.yamlKey" src="/assets/images/locations.webp"
                 [style.--ap-sprite-index]="lm.spriteIndex">
          </div>
          @if (lm.yamlKey !== 'moon_comma_the') {
            <div
              #questContainer class="hover-box landmark-quest"
              [attr.data-location-id]="lm.loc"
              [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]"
              [style.transform]="lm.questMarkerTransform"
              appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(lm.loc, $event, true)"
              (click)="hyperFocus(lm.loc)" (keyup.enter)="hyperFocus(lm.loc)" (keyup.space)="hyperFocus(lm.loc)">
              <!--suppress CheckImageSize -->
              <img width="64" height="64" [alt]="lm.yamlKey" src="/assets/images/locations.webp"
                   [style.--ap-sprite-index]="0">
            </div>
          }
        }
      </div>
      <div #playerTokenContainer class="hover-box player" tabindex="998" [style.z-index]="999"
           appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(null, $event, true)"
           (click)="toggleShowingPath()" (keyup.enter)="toggleShowingPath()" (keyup.space)="toggleShowingPath()">
        <img #playerToken width="64" height="64" alt="player" [src]="playerImageSource.value()">
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
      [cdkConnectedOverlayPositionStrategy]="tooltipPositionStrategy()"
      [cdkConnectedOverlayScrollStrategy]="tooltipScrollStrategy()">
      @if (tooltipOrigin(); as origin) {
        @if (origin.location; as location) {
          <app-location-tooltip [locationKey]="location" />
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

    .dashed-path {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
    }

    .organize {
      display: contents;
    }

    .landmarks {
      --ap-checked-offset: 2;
      --ap-blocked-offset: 2;
    }

    .fillers {
      div {
        background-color: yellow;
      }
    }

    .pause-button-container {
      position: sticky;
      margin-bottom: 0;
      left: 5px;
      bottom: 5px;
      pointer-events: initial;
      width: fit-content;
    }

    .landmark {
      scale: calc(var(--ap-scale, 4) / 4 * 0.75);
      left: calc((var(--ap-left-base, 8px) - 6px) * var(--ap-scale, 4));
      top: calc((var(--ap-top-base, 8px) - 6px) * var(--ap-scale, 4));
      img {
        object-fit: none;
        object-position: calc(-65px * (var(--ap-frame-offset, 0) + var(--ap-checked-offset, 0))) calc(-65px * var(--ap-sprite-index, 0));
        filter: drop-shadow(calc(var(--ap-scale, 4) * 0.8px) calc(var(--ap-scale, 4) * .8px) calc(var(--ap-scale, 4) * 0.5px) black);
      }
    }

    .landmark-quest {
      scale: calc(var(--ap-scale, 4) / 4 * 0.75);
      left: calc((var(--ap-left-base, 8px) - 6px) * var(--ap-scale, 4));
      top: calc((var(--ap-top-base, 8px) - 18px) * var(--ap-scale, 4));
      img {
        object-fit: none;
        object-position: calc(-65px * (var(--ap-frame-offset, 0) + var(--ap-blocked-offset, 0))) 0;
        filter: drop-shadow(calc(var(--ap-scale, 4) * 0.8px) calc(var(--ap-scale, 4) * .8px) calc(var(--ap-scale, 4) * 0.5px) black);
      }
    }

    .filler {
      scale: var(--ap-scale, 4);
      width: 1.6px;
      height: 1.6px;
      border-radius: 0;
      filter: drop-shadow(calc(var(--ap-scale, 4) * 0.1px) calc(var(--ap-scale, 4) * 0.1px) calc(var(--ap-scale, 4) * 0.1px) black);
      left: calc((var(--ap-left-base, 8px) - 0.8px) * var(--ap-scale, 4));
      top: calc((var(--ap-top-base, 8px) - 0.8px) * var(--ap-scale, 4));
    }

    .player {
      scale: calc(var(--ap-scale, 4) / 4);
      left: calc((var(--ap-left-base, 8px) - 8px) * var(--ap-scale, 4));
      top: calc((var(--ap-top-base, 8px) - 8px) * var(--ap-scale, 4));
      will-change: left, top;
      img {
        transform-origin: center;
        filter: drop-shadow(calc(var(--ap-scale, 4) * 0.8px) calc(var(--ap-scale, 4) * .8px) calc(var(--ap-scale, 4) * 0.5px) black);
        transform: rotate(calc(var(--ap-wiggle-amount, 0) * 10deg + var(--ap-neutral-angle, 0rad))) scaleX(var(--ap-scale-x, 1));
      }
    }

    .hover-box {
      position: absolute;
      pointer-events: initial;
      transform-origin: left top;
    }
  `,
})
export class GameTabMap {
  readonly #injector = inject(Injector);
  readonly #store = inject(GameStore);
  readonly #gameScreenStore = inject(GameScreenStore);
  readonly #performanceInsensitiveAnimatableState = inject(PerformanceInsensitiveAnimatableState);
  protected readonly toggleShowingPath = this.#gameScreenStore.toggleShowingPath;
  protected readonly hyperFocus = this.#store.hyperFocus;
  protected readonly running = this.#store.running;

  readonly #allLocations = computed<AllLocationProps | null>(() => {
    if (this.#store.game() === null) {
      // not initialized yet. don't waste time rendering.
      return null;
    }
    const adjustments = {
      makeshift_rocket_ship: {
        offset: [0, -7],
        rotationDegrees: null,
      },
      overweight_boulder: {
        offset: null,
        rotationDegrees: -30,
      },
      captured_goldfish: {
        offset: null,
        rotationDegrees: -30,
      },
      copyright_mouse: {
        offset: null,
        rotationDegrees: -30,
      },
      homeless_mummy: {
        offset: null,
        rotationDegrees: 10,
      },
    } as const satisfies Partial<Record<LandmarkYamlKey, unknown>>;
    const { allLocations, allRegions, startRegion, moonCommaThe } = this.#store.defs();
    const fillers: LocationProps[] = [];
    const landmarks: LandmarkProps[] = [];
    const visited = new BitArray(allRegions.length);
    const q = new Queue<number>();
    function tryEnqueue(r: number) {
      if (!visited[r]) {
        visited[r] = 1;
        q.enqueue(r);
      }
    }
    tryEnqueue(startRegion);
    for (let r = q.dequeue(); r !== undefined; r = q.dequeue()) {
      const region = allRegions[r];
      if (r === moonCommaThe?.region && !this.#performanceInsensitiveAnimatableState.hasCompletedGoal()) {
        continue;
      }
      if ('loc' in region) {
        const transformParts: string[] = [];
        if (region.yamlKey in adjustments) {
          const { offset, rotationDegrees } = adjustments[region.yamlKey as keyof typeof adjustments];
          let finalTranslate = [0, 0];
          if (rotationDegrees !== null) {
            // effectively move the transform origin without affecting anything else
            finalTranslate[0] -= 4;
            finalTranslate[1] -= 16;
            transformParts.push(`translate(calc(4px * var(--ap-scale, 4)), calc(16px * var(--ap-scale, 4))) rotate(${rotationDegrees}deg)`);
          }
          if (offset !== null) {
            finalTranslate[0] += offset[0];
            finalTranslate[1] += offset[1];
          }
          transformParts.push(`translate(calc(${finalTranslate[0]}px * var(--ap-scale, 4)), calc(${finalTranslate[1]}px * var(--ap-scale, 4)))`);
        }
        landmarks.push({
          landmark: r,
          loc: region.loc,
          yamlKey: region.yamlKey,
          coords: allLocations[region.loc].coords,
          spriteIndex: LANDMARKS[region.yamlKey].sprite_index,
          questMarkerTransform: transformParts.length === 0 ? 'none' : transformParts.join(' '),
        });
      }
      else {
        for (const loc of region.locs) {
          fillers.push({ loc, coords: allLocations[loc].coords });
        }
      }
      for (const [nxt] of region.connected.all) {
        tryEnqueue(nxt);
      }
    }
    return { fillers, landmarks };
  });

  readonly allFillers = computed(() => this.#allLocations()?.fillers ?? []);
  readonly allLandmarks = computed(() => this.#allLocations()?.landmarks ?? []);

  // all tooltips here should use the same context so that the user can quickly switch between them
  // without having to sit through the whole delay.
  protected readonly tooltipContext = createEmptyTooltipContext();
  readonly #tooltipOrigin = signal<CurrentTooltipOriginProps | null>(null);
  protected readonly tooltipOrigin = this.#tooltipOrigin.asReadonly();
  protected readonly tooltipPositionStrategy = computed(() => {
    return createFlexibleConnectedPositionStrategy(this.#injector, this.#tooltipOrigin()?.element ?? this.outerDiv().nativeElement)
      .withPositions([{
        originX: 'start',
        originY: 'bottom',
        overlayX: 'start',
        overlayY: 'top',
      }]);
  });

  protected readonly tooltipScrollStrategy = computed(() => {
    return createRepositionScrollStrategy(this.#injector);
  });

  protected readonly outerDiv = viewChild.required<ElementRef<HTMLDivElement>>('outer');
  protected readonly dashedPath = viewChild.required<ElementRef<SVGPathElement>>('dashedPath');
  protected readonly overlay = viewChild.required(CdkConnectedOverlay);
  protected readonly fillerSquares = viewChildren<ElementRef<HTMLDivElement>>('fillerSquare');
  protected readonly landmarkContainers = viewChildren<ElementRef<HTMLDivElement>>('landmarkContainer');
  protected readonly questContainers = viewChildren<ElementRef<HTMLDivElement>>('questContainer');
  protected readonly playerTokenContainer = viewChild.required<ElementRef<HTMLDivElement>>('playerTokenContainer');
  #overlayIsAttached = false;

  readonly playerImageSource = resource({
    params: () => this.#store.playerTokenValue(),
    loader: async ({ params: playerToken }) => {
      if (playerToken === null) {
        return null;
      }
      const blob = await playerToken.canvas.convertToBlob();
      return URL.createObjectURL(blob);
    },
  });

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
    // whenever the outer div resizes, we also need to resize the app to match.
    const outerDivSize = elementSizeSignal(this.outerDiv);
    effect(() => {
      const victoryLocationYamlKey = this.#store.victoryLocationYamlKey();
      const { clientHeight } = outerDivSize();
      const scale = clientHeight / VICTORY_LOCATION_CROP_LOOKUP[victoryLocationYamlKey];
      this.outerDiv().nativeElement.style.setProperty('--ap-scale', scale.toString());
      if (this.#overlayIsAttached) {
        this.overlay().overlayRef.updatePosition();
      }
    });

    const initAnimationWatcherEffect = effect(() => {
      const outerDiv = this.outerDiv().nativeElement;
      const dashedPath = this.dashedPath().nativeElement;
      const overlay = this.overlay();
      const playerTokenContainer = this.playerTokenContainer().nativeElement;
      const landmarkContainers = this.landmarkContainers().map(l => l.nativeElement);
      const questContainers = this.questContainers().map(l => l.nativeElement);
      const fillerSquares = this.fillerSquares().map(l => l.nativeElement);

      if (landmarkContainers.length === 0 || questContainers.length === 0 || fillerSquares.length === 0) {
        // still waiting to know how many landmarks / fillers there are.
        return;
      }

      runInInjectionContext(this.#injector, () => {
        watchAnimations({
          outerDiv,
          dashedPath,
          overlay,
          playerTokenContainer,
          landmarkContainers,
          questContainers,
          fillerSquares,
        });
      });
      initAnimationWatcherEffect.destroy();
    });
  }

  protected togglePause() {
    this.#store.togglePause();
  }

  protected setTooltipOrigin(location: number | null, props: TooltipOriginProps | null, fromDirective: boolean) {
    this.#tooltipOrigin.update((prev) => {
      if (prev !== null && !fromDirective) {
        prev.notifyDetached();
      }
      return props === null
        ? null
        : { location, ...props };
    });
  }
}

interface AllLocationProps {
  fillers: readonly LocationProps[];
  landmarks: readonly LandmarkProps[];
}

interface LocationProps {
  loc: number;
  coords: Vec2;
}

interface LandmarkProps extends LocationProps {
  landmark: number;
  yamlKey: LandmarkYamlKey;
  spriteIndex: number;
  questMarkerTransform: string;
}

interface CurrentTooltipOriginProps {
  location: number | null;
  element: HTMLElement;
  notifyDetached: () => void;
}
