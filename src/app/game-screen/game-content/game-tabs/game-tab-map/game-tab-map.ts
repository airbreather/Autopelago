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
            class="map-positioned filler"
            [style.--ap-left-base.px]="f.coords[0]" [style.--ap-top-base.px]="f.coords[1]">
            <div
              #fillerSquare [tabindex]="$index + 1999" class="pointer-interactive"
              [class.hyper-focus]="hyperFocusLocation() === f.loc" [attr.data-location-id]="f.loc"
              appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(f.loc, $event, true)"
              (click)="setOrClearHyperFocus(f.loc)" (keyup.enter)="setOrClearHyperFocus(f.loc)" (keyup.space)="setOrClearHyperFocus(f.loc)">
            </div>
          </div>
        }
      </div>
      <svg #dashedPathSvg class="dashed-path" viewBox="0 0 300 450" preserveAspectRatio="none">
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
            #landmarkContainer class="map-positioned landmark"
            [attr.data-location-id]="lm.loc" [style.--ap-checked-offset]="lm.yamlKey === 'moon_comma_the' ? 0 : 'unset'"
            [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]">
            <!--suppress CheckImageSize, AngularNgOptimizedImage -->
            <img width="64" height="64" [alt]="lm.yamlKey" src="assets/images/locations.webp"
                 [style.--ap-sprite-index]="lm.spriteIndex"
                 [tabindex]="$index + 999" class="pointer-interactive"
                 [class.hyper-focus]="hyperFocusLocation() === lm.loc"
                 appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(lm.loc, $event, true)"
                 (click)="setOrClearHyperFocus(lm.loc)" (keyup.enter)="setOrClearHyperFocus(lm.loc)" (keyup.space)="setOrClearHyperFocus(lm.loc)">
          </div>
          @if (lm.yamlKey !== 'moon_comma_the') {
            <div
              #questContainer class="map-positioned landmark-quest"
              [attr.data-location-id]="lm.loc"
              [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]"
              [style.transform]="lm.questMarkerTransform">
              <!--
              eslint-disable
                @angular-eslint/template/interactive-supports-focus,
                @angular-eslint/template/click-events-have-key-events,
              --
              the intended way to interact is via the landmark containers themselves, which totally
              support tab focusing. the mouse is allowed to interact with the quest markers for one
              specific reason: the player token sometimes covers up the landmark, so this is the ONLY
              way to get at that landmark with the mouse. the keyboard has no such limitation, so this
              is preferable to adding duplicate things in the tab order.
              -->
              <!--suppress CheckImageSize, AngularNgOptimizedImage -->
              <img width="16" height="48" [alt]="lm.yamlKey" src="assets/images/locations.webp"
                   [style.--ap-sprite-index]="0" class="pointer-interactive"
                   appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(lm.loc, $event, true)"
                   (click)="setOrClearHyperFocus(lm.loc)">
              <!-- eslint-enable
                @angular-eslint/template/interactive-supports-focus,
                @angular-eslint/template/click-events-have-key-events,
              -->
            </div>
          }
        }
      </div>
      <div #playerTokenContainer class="map-positioned player" [style.z-index]="999">
        <!--suppress AngularNgOptimizedImage -->
        <img width="64" height="64" alt="player" [src]="playerImageSource.value()" tabindex="998" class="pointer-interactive"
             appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(null, $event, true)"
             (click)="toggleShowingPath()" (keyup.enter)="toggleShowingPath()" (keyup.space)="toggleShowingPath()">
      </div>
      <div #ratPoisonContainer class="map-positioned rat-poison" [style.z-index]="999" [style.display]="'none'">
        <!--suppress AngularNgOptimizedImage -->
        <img width="64" height="64" alt="rat poison" src="assets/images/rat_poison.webp">
      </div>
      <div #pauseButtonContainer class="pause-button-container"
           [style.margin-top]="'-' + pauseButtonContainer.clientHeight + 'px'"
           [style.z-index]="1000">
        <button class="rat-toggle-button"
                [class.toggled-on]="!running()"
                (click)="togglePause()">
          ⏸
        </button>
      </div>
      <div #fadeToBlack class="fade-to-black">
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
          <app-location-tooltip
            [locationKey]="location"
            (mouseenter)="tooltipContext.notifyMouseEnterTooltip(origin.uid)"
            (mouseleave)="tooltipContext.notifyMouseLeaveTooltip(origin.uid)"
          />
        }
        @else {
          <app-player-tooltip
            (mouseenter)="tooltipContext.notifyMouseEnterTooltip(origin.uid)"
            (mouseleave)="tooltipContext.notifyMouseLeaveTooltip(origin.uid)"
          />
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

    .fade-to-black {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      background-color: black;
      opacity: 0;
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
      left: calc(var(--ap-left-base, 8px) * var(--ap-scale, 4));
      top: calc(var(--ap-top-base, 8px) * var(--ap-scale, 4));
      img {
        object-fit: none;
        object-position: calc(-65px * (var(--ap-frame-offset, 0) + var(--ap-checked-offset, 0))) calc(-65px * var(--ap-sprite-index, 0));
        filter: drop-shadow(6px 6px 4px black);
        // base coords point to the center point of the location, but it gets drawn at the top-left.
        // shift by 50% of the height and width to accommodate.
        transform: translate(-50%, -50%);
      }
    }

    .landmark-quest {
      scale: calc(var(--ap-scale, 4) / 4 * 0.75);
      left: calc(var(--ap-left-base, 8px) * var(--ap-scale, 4));
      top: calc(var(--ap-top-base, 8px) * var(--ap-scale, 4));
      img {
        object-fit: none;
        object-position: calc(-65px * (var(--ap-frame-offset, 0) + var(--ap-blocked-offset, 0)) - 24px) -4px;
        filter: drop-shadow(6px 6px 3px black);
        // do the same 50% shift as with the landmarks themselves, then shift it up further by the full
        // height of the landmark (which is our own height scaled by the ratio of the two heights).
        transform: translate(-50%, -50%) translateY(calc(-100% * 64 / 48));
      }
    }

    .filler {
      width: calc(1.6px * var(--ap-scale, 4));
      height: calc(1.6px * var(--ap-scale, 4));
      border-radius: 0;
      left: calc(var(--ap-left-base, 8px) * var(--ap-scale, 4));
      top: calc(var(--ap-top-base, 8px) * var(--ap-scale, 4));
      > div {
        width: 100%;
        height: 100%;
        border-radius: 0;
        background-color: yellow;
        filter: drop-shadow(3.4px 3.4px 3.4px black);
        transform: translate(-50%, -50%);
      }
    }

    .player,.rat-poison {
      scale: calc(var(--ap-scale, 4) / 4);
      left: calc(var(--ap-left-base, 8px) * var(--ap-scale, 4));
      top: calc(var(--ap-top-base, 8px) * var(--ap-scale, 4));
      //noinspection CssInvalidPropertyValue
      will-change: left, top;
      img {
        transform-origin: center;
        filter: drop-shadow(6px 6px 3px black);
        transform: translate(-50%, -50%) rotate(calc(var(--ap-wiggle-amount, 0) * 10deg + var(--ap-neutral-angle, 0rad))) scaleX(var(--ap-scale-x, 1));
      }
    }

    .map-positioned {
      position: absolute;
      transform-origin: left top;
    }

    .pointer-interactive {
      pointer-events: initial;
    }

    .hyper-focus {
      outline: 6px dashed red;
      outline-offset: 6px;
    }
  `,
})
export class GameTabMap {
  readonly #injector = inject(Injector);
  readonly #store = inject(GameStore);
  readonly #gameScreenStore = inject(GameScreenStore);
  readonly #performanceInsensitiveAnimatableState = inject(PerformanceInsensitiveAnimatableState);
  protected readonly toggleShowingPath = this.#gameScreenStore.toggleShowingPath;
  protected readonly hyperFocusLocation = this.#store.hyperFocusLocation;
  protected readonly setOrClearHyperFocus = this.#store.setOrClearHyperFocus;
  protected readonly running = this.#store.running;

  readonly #allLocations = computed<AllLocationProps | null>(() => {
    if (this.#store.game() === null) {
      // not initialized yet. don't waste time rendering.
      return null;
    }
    const questMarkerAdjustments: Partial<Record<LandmarkYamlKey, string>> = {
      makeshift_rocket_ship: 'translateY(-100%)',
      overweight_boulder: 'rotate(-30deg)',
      captured_goldfish: 'rotate(-30deg)',
      copyright_mouse: 'rotate(-30deg)',
      homeless_mummy: 'rotate(10deg)',
    };
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
        landmarks.push({
          landmark: r,
          loc: region.loc,
          yamlKey: region.yamlKey,
          coords: allLocations[region.loc].coords,
          spriteIndex: LANDMARKS[region.yamlKey].sprite_index,
          questMarkerTransform: questMarkerAdjustments[region.yamlKey] ?? 'none',
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
  protected readonly dashedPathSvg = viewChild.required<ElementRef<SVGSVGElement>>('dashedPathSvg');
  protected readonly overlay = viewChild.required(CdkConnectedOverlay);
  protected readonly fillerSquares = viewChildren<ElementRef<HTMLDivElement>>('fillerSquare');
  protected readonly landmarkContainers = viewChildren<ElementRef<HTMLDivElement>>('landmarkContainer');
  protected readonly questContainers = viewChildren<ElementRef<HTMLDivElement>>('questContainer');
  protected readonly playerTokenContainer = viewChild.required<ElementRef<HTMLDivElement>>('playerTokenContainer');
  protected readonly ratPoisonContainer = viewChild.required<ElementRef<HTMLDivElement>>('ratPoisonContainer');
  protected readonly fadeToBlack = viewChild.required<ElementRef<HTMLDivElement>>('fadeToBlack');
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
      const game = this.#store.game();
      const dashedPath = this.dashedPath().nativeElement;
      const overlay = this.overlay();
      const fadeToBlack = this.fadeToBlack().nativeElement;
      const playerTokenContainer = this.playerTokenContainer().nativeElement;
      const ratPoisonContainer = this.ratPoisonContainer().nativeElement;
      const landmarkContainers = this.landmarkContainers().map(l => l.nativeElement);
      const questContainers = this.questContainers().map(l => l.nativeElement);
      const fillerSquares = this.fillerSquares().map(l => l.nativeElement);

      if (game === null || landmarkContainers.length === 0 || questContainers.length === 0 || fillerSquares.length === 0) {
        // still waiting to know how many landmarks / fillers there are.
        return;
      }

      runInInjectionContext(this.#injector, () => {
        watchAnimations({
          dashedPath,
          overlay,
          fadeToBlack,
          playerTokenContainer,
          ratPoisonContainer,
          landmarkContainers,
          questContainers,
          fillerSquares,
          enableTileAnimations: game.connectScreenState.enableTileAnimations,
          enableRatAnimations: game.connectScreenState.enableRatAnimations,
        });
      });
      initAnimationWatcherEffect.destroy();
    });

    effect(() => {
      // #142: the viewbox actually needs to be based on the victory location.
      const dashedPathSvg = this.dashedPathSvg().nativeElement;
      const victoryLocationYamlKey = this.#store.victoryLocationYamlKey();
      dashedPathSvg.setAttribute('viewBox', `0 0 300 ${VICTORY_LOCATION_CROP_LOOKUP[victoryLocationYamlKey].toString()}`);
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
  uid: symbol;
  location: number | null;
  element: HTMLElement;
  notifyDetached: () => void;
}
