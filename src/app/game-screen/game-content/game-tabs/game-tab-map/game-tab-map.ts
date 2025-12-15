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
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import BitArray from '@bitarray/typedarray';

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
      @for (lm of allLandmarks(); track lm.yamlKey) {
        <div
          class="hover-box landmark" [tabindex]="$index + 999"
          [style.--ap-left-base.px]="lm.coords[0]" [style.--ap-top-base.px]="lm.coords[1]">
          <!--suppress CheckImageSize -->
          <img #landmarkImage width="64" height="64" [alt]="lm.yamlKey" src="/assets/images/locations.webp"
               [id]="'landmark-image-' + lm.loc" [style.--ap-sprite-index]="lm.spriteIndex"
               appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(lm.landmark, $event, true)">
        </div>
      }
      <div #playerTokenHoverBox class="hover-box" tabindex="998" [style.z-index]="999"
           appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin(null, $event, true)"
           (click)="toggleShowingPath()" (keyup.enter)="toggleShowingPath()" (keyup.space)="toggleShowingPath()">
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

    .pause-button-container {
      position: sticky;
      margin-bottom: 0;
      left: 5px;
      bottom: 5px;
      pointer-events: initial;
      width: fit-content;
    }

    .landmark {
      img {
        object-fit: none;
        object-position: calc(-65px * (var(--ap-frame-offset, 0) + var(--ap-checked-offset, 0))) calc(-65px * var(--ap-sprite-index, 0));
        overflow: hidden;
        filter: drop-shadow(calc(var(--ap-scale, 4) * 1px) calc(var(--ap-scale, 4) * 1px) calc(var(--ap-scale, 4) * 0.5px) black);
      }
    }

    .hover-box {
      position: absolute;
      pointer-events: initial;

      transform-origin: left top;
      scale: calc(var(--ap-scale, 4) / 4);
      left: calc((var(--ap-left-base, 8px) - 8px) * var(--ap-scale, 4));
      top: calc((var(--ap-top-base, 8px) - 8px) * var(--ap-scale, 4));
    }
  `,
})
export class GameTabMap {
  readonly #injector = inject(Injector);
  readonly #store = inject(GameStore);
  readonly #gameScreenStore = inject(GameScreenStore);
  protected readonly toggleShowingPath = this.#gameScreenStore.toggleShowingPath;
  protected readonly running = this.#store.running;

  readonly allLandmarks = computed(() => {
    const { allLocations, allRegions, startRegion, moonCommaThe } = this.#store.defs();
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
      if (r === moonCommaThe?.region && !this.#store.hasCompletedGoal()) {
        continue;
      }
      if ('loc' in region) {
        result.push({
          landmark: r,
          loc: region.loc,
          yamlKey: region.yamlKey,
          coords: allLocations[region.loc].coords,
          spriteIndex: LANDMARKS[region.yamlKey].sprite_index,
        });
      }
      for (const [nxt] of region.connected.all) {
        tryEnqueue(nxt);
      }
    }
    return result;
  });

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
  protected readonly landmarkImages = viewChildren<ElementRef<HTMLImageElement>>('landmarkImage');

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

    let interval: number | null = null;
    effect(() => {
      const outerDiv = this.outerDiv().nativeElement;
      if (this.#store.running()) {
        if (interval !== null) {
          clearInterval(interval);
        }
        let frame1 = true;
        interval = setInterval(() => {
          outerDiv.style.setProperty('--ap-frame-offset', frame1 ? '0' : '1');
          frame1 = !frame1;
        }, 500);
      }
      else if (interval !== null) {
        clearInterval(interval);
        interval = null;
      }
    });

    const locationIsCheckedSignals = this.#store.defs().allLocations.map(l => computed(() => {
      return this.#store.locationIsChecked()[l.key];
    }));
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

interface LandmarkProps {
  landmark: number;
  loc: number;
  yamlKey: LandmarkYamlKey;
  coords: Vec2;
  spriteIndex: number;
}

interface CurrentTooltipOriginProps {
  landmark: number | null;
  element: HTMLElement;
  notifyDetached: () => void;
}
