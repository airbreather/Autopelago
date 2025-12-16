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
} from '@angular/core';
import BitArray from '@bitarray/typedarray';

import gsap from 'gsap';
import Queue from 'yocto-queue';
import { LANDMARKS, type LandmarkYamlKey, type Vec2 } from '../../../../data/locations';
import { BAKED_DEFINITIONS_FULL } from '../../../../data/resolved-definitions';
import { GameStore } from '../../../../store/autopelago-store';
import { GameScreenStore } from '../../../../store/game-screen-store';
import { createEmptyTooltipContext, TooltipBehavior, type TooltipOriginProps } from '../../../../tooltip-behavior';
import { elementSizeSignal } from '../../../../utils/element-size';
import { AnimationSequencedGameState } from '../../animation-sequenced-game-state';
import { LandmarkTooltip } from './landmark-tooltip';
import { PlayerTooltip } from './player-tooltip';

function extractObservedBits(gameStore: InstanceType<typeof GameStore>) {
  return {
    defs: gameStore.defs(),
    ratCount: gameStore.ratCount(),
    food: gameStore.foodFactor(),
    energy: gameStore.energyFactor(),
    luck: gameStore.luckFactor(),
    distraction: gameStore.distractionCounter(),
    startled: gameStore.startledCounter(),
    smart: gameStore.targetLocationChosenBecauseSmart(),
    conspiratorial: gameStore.targetLocationChosenBecauseConspiratorial(),
    stylish: gameStore.styleFactor(),
    confidence: gameStore.hasConfidence(),
    outgoingMovementActions: gameStore.consumeOutgoingMovementActions(),
    receivedItemCountLookup: gameStore.receivedItemCountLookup(),
    checkedLocations: gameStore.checkedLocations(),
    regionIsLandmarkWithRequirementSatisfied: gameStore.regionLocks().regionIsLandmarkWithRequirementSatisfied,
  };
}

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
      <div class="organize fillers">
        @for (f of allFillers(); track f.loc) {
          <div
            class="hover-box filler" [tabindex]="$index + 1999"
            [style.--ap-left-base.px]="f.coords[0]" [style.--ap-top-base.px]="f.coords[1]"
            #fillerSquare [id]="'filler-div-' + f.loc">
          </div>
        }
      </div>
      <div class="organize landmarks">
        @for (lm of allLandmarks(); track lm.loc) {
          <div
            class="hover-box landmark" [tabindex]="$index + 999"
            [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]">
            <!--suppress CheckImageSize -->
            <img width="64" height="64" [alt]="lm.yamlKey" src="/assets/images/locations.webp"
                 [id]="'landmark-image-' + lm.loc" [style.--ap-sprite-index]="lm.spriteIndex"
                 appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(lm.landmark, $event, true)">
          </div>
          <div
            class="hover-box landmark-quest"
            [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]">
            <!--suppress CheckImageSize -->
            <img width="64" height="64" [alt]="lm.yamlKey" src="/assets/images/locations.webp"
                 [id]="'landmark-quest-image-' + lm.loc" [style.--ap-sprite-index]="0"
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
      scale: calc(var(--ap-scale, 4) / 4);
      left: calc((var(--ap-left-base, 8px) - 8px) * var(--ap-scale, 4));
      top: calc((var(--ap-top-base, 8px) - 8px) * var(--ap-scale, 4));
      img {
        object-fit: none;
        object-position: calc(-65px * (var(--ap-frame-offset, 0) + var(--ap-checked-offset, 0))) calc(-65px * var(--ap-sprite-index, 0));
        filter: drop-shadow(calc(var(--ap-scale, 4) * 0.8px) calc(var(--ap-scale, 4) * .8px) calc(var(--ap-scale, 4) * 0.5px) black);
      }
    }

    .landmark-quest {
      scale: calc(var(--ap-scale, 4) / 4 * 0.75);
      left: calc((var(--ap-left-base, 8px) - 6px) * var(--ap-scale, 4));
      top: calc((var(--ap-top-base, 8px) - 21px) * var(--ap-scale, 4));
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
  readonly #anim = inject(AnimationSequencedGameState);
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
        gsap.timeline({ repeat: -1, defaults: { ease: 'none', duration: 0.25 } })
          .to(outerDiv, { ['--ap-wiggle-amount']: 1 })
          .to(outerDiv, { ['--ap-wiggle-amount']: 0 })
          .to(outerDiv, { ['--ap-wiggle-amount']: -1 })
          .to(outerDiv, { ['--ap-wiggle-amount']: 0 }),
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
    const locationsMarkedChecked = new BitArray(BAKED_DEFINITIONS_FULL.allLocations.length + 1);
    const landmarksMarkedSatisfied = new BitArray(BAKED_DEFINITIONS_FULL.allRegions.length + 1);
    effect(() => {
      const playerTokenContainer = this.playerTokenContainer().nativeElement;
      const observedBits = extractObservedBits(this.#store);
      const { allLocations, allRegions, regionForLandmarkLocation } = observedBits.defs;
      for (const move of observedBits.outgoingMovementActions) {
        const fromCoords = allLocations[move.fromLocation].coords;
        const toCoords = allLocations[move.toLocation].coords;
        let neutralAngle = Math.atan2(toCoords[1] - fromCoords[1], toCoords[0] - fromCoords[0]);
        let scaleX = 1;
        if (Math.abs(neutralAngle) >= Math.PI / 2) {
          neutralAngle -= Math.PI;
          scaleX = -1;
        }
        this.#oneShotTimeline
          .set(
            playerTokenContainer, {
              ['--ap-neutral-angle']: `${neutralAngle.toString()}rad`,
              ['--ap-scale-x']: scaleX.toString(),
              immediateRender: false,
              duration: 0,
            },
            '>',
          );
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
              duration: 0.1,
            },
            '>',
          );
      }

      const newlyCheckedLocations = [...observedBits.checkedLocations.filter(l => !locationsMarkedChecked[l])];
      if (newlyCheckedLocations.length > 0) {
        const newlyCheckedLandmarks = newlyCheckedLocations.filter(l => !Number.isNaN(regionForLandmarkLocation[l]));
        if (newlyCheckedLandmarks.length > 0) {
          this.#oneShotTimeline.to(
            newlyCheckedLandmarks.map(l => `#landmark-image-${l.toString()}`),
            { ['--ap-checked-offset']: 0, duration: 0 },
            '>',
          );
          this.#oneShotTimeline.to(
            newlyCheckedLandmarks.map(l => `#landmark-quest-image-${l.toString()}`),
            { display: 'none', duration: 0 },
            '>',
          );
        }
        const newlyCheckedFillers = newlyCheckedLocations.filter(l => Number.isNaN(regionForLandmarkLocation[l]));
        if (newlyCheckedFillers.length > 0) {
          this.#oneShotTimeline.to(
            newlyCheckedFillers.map(l => `#filler-div-${l.toString()}`),
            { ['background-color']: 'gray', duration: 0 },
            '>',
          );
        }
      }

      const newlyMarkedLandmarks: number[] = [];
      for (let i = 0; i < observedBits.regionIsLandmarkWithRequirementSatisfied.length; i++) {
        if (landmarksMarkedSatisfied[i]) {
          continue;
        }

        if (observedBits.regionIsLandmarkWithRequirementSatisfied[i]) {
          const region = allRegions[i];
          if ('loc' in region) {
            newlyMarkedLandmarks.push(region.loc);
          }
          landmarksMarkedSatisfied[i] = 1;
        }
      }
      if (newlyMarkedLandmarks.length > 0) {
        this.#oneShotTimeline.to(
          newlyMarkedLandmarks.map(l => `#landmark-quest-image-${l.toString()}`),
          { ['--ap-blocked-offset']: 0, duration: 0 },
          '>',
        );
      }

      this.#oneShotTimeline.to(this, {
        duration: 0,
        onUpdate: () => {
          this.#apply(observedBits);
        },
      }, '>');
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

  #apply(state: ReturnType<typeof extractObservedBits>) {
    const { ratCount, food, energy, luck, distraction, startled, smart, conspiratorial, stylish, confidence } = state;
    this.#anim.ratCount.set(ratCount);
    this.#anim.food.set(food);
    this.#anim.energy.set(energy);
    this.#anim.luck.set(luck);
    this.#anim.distraction.set(distraction);
    this.#anim.startled.set(startled);
    this.#anim.smart.set(smart);
    this.#anim.conspiratorial.set(conspiratorial);
    this.#anim.stylish.set(stylish);
    this.#anim.confidence.set(confidence);
    for (let i = 0; i < state.receivedItemCountLookup.length; ++i) {
      this.#anim.itemCount[i].set(state.receivedItemCountLookup[i]);
    }
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
