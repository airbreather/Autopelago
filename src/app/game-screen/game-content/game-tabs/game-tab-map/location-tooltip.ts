import { NgOptimizedImage } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import type { AutopelagoLandmarkRegion } from '../../../../data/resolved-definitions';
import { GameStore } from '../../../../store/autopelago-store';
import { RequirementDisplay } from './requirement-display';

@Component({
  selector: 'app-location-tooltip',
  imports: [
    RequirementDisplay,
    NgOptimizedImage,
  ],
  template: `
    <div class="outer">
      <h1 class="box header">{{ location().name }}</h1>
      @if (landmarkRegion(); as region) {
        <div class="box main-content">
          <div class="image-and-requirement">
            <img class="landmark-image" [alt]="region.yamlKey"
                 width="64" height="64"
                 [ngSrc]="'assets/images/locations/' + region.yamlKey + '.webp'">
            <app-requirement-display class="requirement-display" [requirement]="region.requirement"/>
          </div>
          <div class="flavor-text" [hidden]="!location().flavorText">“{{ location().flavorText }}”</div>
        </div>
      }
    </div>
  `,
  styles: `
    @use '../../../../../theme';

    .outer {
      display: grid;
      grid-template-columns: min-content;
      grid-auto-rows: auto;
      gap: 5px;
      padding: 4px;
      background-color: theme.$region-color;
    }

    .box {
      padding: 4px;
      border: 2px solid black;
    }

    .header {
      margin: 0;
      font-size: 14pt;
      white-space: nowrap;
    }

    .image-and-requirement {
      display: grid;
      grid-template-columns: auto 1fr;
      align-items: start;
    }

    .requirement-display {
      margin-left: 5px;
      white-space: nowrap;
    }

    .flavor-text {
      margin-top: 10px;
      font-size: 8pt;
    }
  `,
})
export class LocationTooltip {
  readonly #store = inject(GameStore);
  readonly locationKey = input.required<number>();
  protected readonly location = computed(() => this.#store.defs().allLocations[this.locationKey()]);
  protected readonly landmarkRegion = computed(() => {
    const { allRegions, regionForLandmarkLocation } = this.#store.defs();
    const regionKeyIfLandmark = regionForLandmarkLocation[this.locationKey()];
    return Number.isNaN(regionKeyIfLandmark)
      ? null
      : allRegions[regionKeyIfLandmark] as AutopelagoLandmarkRegion;
  });
}
