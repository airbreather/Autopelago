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
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import BitArray from '@bitarray/typedarray';

import gsap from 'gsap';
import Queue from 'yocto-queue';
import { LANDMARKS, type LandmarkYamlKey, type Vec2 } from '../../../../data/locations';
import { GameStore } from '../../../../store/autopelago-store';
import { GameScreenStore } from '../../../../store/game-screen-store';
import { createEmptyTooltipContext, TooltipBehavior, type TooltipOriginProps } from '../../../../tooltip-behavior';
import { elementSizeSignal } from '../../../../utils/element-size';
import { LandmarkTooltip } from './landmark-tooltip';
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
      <div id="fillers" class="just-for-organization">
        @for (f of allFillers(); track f.loc) {
          <div
            class="hover-box filler" [tabindex]="$index + 1999"
            [style.--ap-left-base.px]="f.coords[0]" [style.--ap-top-base.px]="f.coords[1]"
            #fillerSquare [id]="'filler-div-' + f.loc">
          </div>
        }
      </div>
      <div id="landmarks" class="just-for-organization">
        @for (lm of allLandmarks(); track lm.loc) {
          <div
            class="hover-box landmark" [tabindex]="$index + 999"
            [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]">
            <!--suppress CheckImageSize -->
            <img #landmarkImage width="64" height="64" [alt]="lm.yamlKey" src="/assets/images/locations.webp"
                 [id]="'landmark-image-' + lm.loc" [style.--ap-sprite-index]="lm.spriteIndex"
                 appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(lm.landmark, $event, true)">
          </div>
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

    .just-for-organization {
      display: contents;
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
      scale: calc(var(--ap-scale, 4) / 4);
      left: calc((var(--ap-left-base, 8px) - 8px) * var(--ap-scale, 4));
      top: calc((var(--ap-top-base, 8px) - 8px) * var(--ap-scale, 4));
      img {
        object-fit: none;
        object-position: calc(-65px * (var(--ap-frame-offset, 0) + var(--ap-checked-offset, 0))) calc(-65px * var(--ap-sprite-index, 0));
        overflow: hidden;
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
      img {
        transform-origin: center;
        filter: drop-shadow(calc(var(--ap-scale, 4) * 0.8px) calc(var(--ap-scale, 4) * .8px) calc(var(--ap-scale, 4) * 0.5px) black);
        rotate: calc(var(--ap-wiggle-amount, 0) * 10deg);
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
  protected readonly toggleShowingPath = this.#gameScreenStore.toggleShowingPath;
  protected readonly running = this.#store.running;

  readonly #allLocations = computed<AllLocationProps>(() => {
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
      if (r === moonCommaThe?.region && !this.#store.hasCompletedGoal()) {
        continue;
      }
      if ('loc' in region) {
        landmarks.push({
          landmark: r,
          loc: region.loc,
          yamlKey: region.yamlKey,
          coords: allLocations[region.loc].coords,
          spriteIndex: LANDMARKS[region.yamlKey].sprite_index,
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

  readonly allFillers = computed(() => this.#allLocations().fillers);
  readonly allLandmarks = computed(() => this.#allLocations().landmarks);

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

  readonly #repeatTimeline = gsap.timeline();
  readonly #oneShotTimeline = gsap.timeline();
  protected readonly outerDiv = viewChild.required<ElementRef<HTMLDivElement>>('outer');
  protected readonly fillerSquares = viewChildren<ElementRef<HTMLDivElement>>('fillerSquare');
  protected readonly landmarkImages = viewChildren<ElementRef<HTMLImageElement>>('landmarkImage');
  protected readonly playerTokenContainer = viewChild.required<ElementRef<HTMLDivElement>>('playerTokenContainer');

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
    const sizeSignal = elementSizeSignal(this.outerDiv);
    const width = computed(() => sizeSignal().clientWidth);
    effect(() => {
      const { nativeElement: outerDiv } = this.outerDiv();
      outerDiv.style.setProperty('--ap-scale', (width() / 300).toString());
    });
    effect(() => {
      const outerDiv = this.outerDiv().nativeElement;
      this.#repeatTimeline.add(
        gsap.timeline({ repeat: -1 })
          .to(outerDiv, { ['--ap-wiggle-amount']: 1, ease: 'none', duration: 0.25 })
          .to(outerDiv, { ['--ap-wiggle-amount']: 0, ease: 'none', duration: 0.25 })
          .to(outerDiv, { ['--ap-wiggle-amount']: -1, ease: 'none', duration: 0.25 })
          .to(outerDiv, { ['--ap-wiggle-amount']: 0, ease: 'none', duration: 0.25 }),
        0,
      );
      this.#repeatTimeline.add(
        gsap.timeline({ repeat: -1 })
          .set(outerDiv, { ['--ap-frame-offset']: 1 }, 0.5)
          .set(outerDiv, { ['--ap-frame-offset']: 0 }, 1),
        0,
      );
    });
    effect(() => {
      if (this.#store.running()) {
        this.#repeatTimeline.play();
        this.#oneShotTimeline.play();
      }
      else {
        this.#repeatTimeline.pause();
        this.#oneShotTimeline.pause();
      }
    });
    effect(() => {
      const playerTokenContainer = this.playerTokenContainer().nativeElement;
      const { allLocations } = this.#store.defs();
      for (const anim of this.#store.consumeOutgoingAnimatableActions()) {
        switch (anim.type) {
          case 'move': {
            const fromCoords = allLocations[anim.fromLocation].coords;
            const toCoords = allLocations[anim.toLocation].coords;
            this.#oneShotTimeline
              .fromTo(
                playerTokenContainer, {
                  ['--ap-left-base']: `${fromCoords[0].toString()}px`,
                  ['--ap-top-base']: `${fromCoords[1].toString()}px`,
                  immediateRender: false,
                }, {
                  ['--ap-left-base']: `${toCoords[0].toString()}px`,
                  ['--ap-top-base']: `${toCoords[1].toString()}px`,
                  ease: 'none',
                  immediateRender: false,
                },
                '>',
              );
            break;
          }

          case 'check-locations': {
            this.#oneShotTimeline
              .call(() => {
                anim.locations.forEach((loc) => {
                  locationIsCheckedSignals[loc].set(true);
                });
              }, [], '>');
            break;
          }
        }
      }
    });

    const locationIsCheckedSignals = this.#store.defs().allLocations.map(() => signal(false));
    effect(() => {
      for (const { nativeElement: image } of this.landmarkImages()) {
        const id = Number(image.id.slice('landmark-image-'.length));
        const locationIsCheckedSignal = locationIsCheckedSignals[id];
        setTimeout(() => {
          effect(() => {
            image.style.setProperty('--ap-checked-offset', locationIsCheckedSignal() ? '0' : '2');
          }, { injector: this.#injector });
        });
      }
      for (const { nativeElement: square } of this.fillerSquares()) {
        const id = Number(square.id.slice('filler-div-'.length));
        const locationIsCheckedSignal = locationIsCheckedSignals[id];
        setTimeout(() => {
          effect(() => {
            square.style.setProperty('background-color', locationIsCheckedSignal() ? 'grey' : 'yellow');
          }, { injector: this.#injector });
        });
      }
    });
  }

  protected togglePause() {
    this.#store.togglePause();
  }

  protected setTooltipOrigin(landmark: number | null, props: TooltipOriginProps | null, fromDirective: boolean) {
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
}

interface CurrentTooltipOriginProps {
  landmark: number | null;
  element: HTMLElement;
  notifyDetached: () => void;
}
