import { NgOptimizedImage } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import type { AutopelagoLandmarkRegion } from '../../../../../data/resolved-definitions';
import { GameStore } from '../../../../../store/autopelago-store';
import { RequirementDisplay } from './requirement-display';

@Component({
  selector: 'app-tooltip',
  imports: [
    RequirementDisplay,
    NgOptimizedImage,
  ],
  template: `
    <div class="outer">
      <h1 class="box header">{{landmarkLocation().name}}</h1>
      <div class="box main-content">
        <div class="image-and-requirement">
          <img class="landmark-image" [alt]="landmark().yamlKey"
               width="64" height="64"
               [ngSrc]="'assets/images/locations/' + landmark().yamlKey + '.webp'">
          <app-requirement-display class="requirement-display" [requirement]="landmark().requirement" />
        </div>
        <div class="flavor-text" [hidden]="!landmarkLocation().flavorText">“{{landmarkLocation().flavorText}}”</div>
      </div>
    </div>
  `,
  styles: `
    @import '../../../../../../theme.scss';
    .outer {
      display: grid;
      grid-template-columns: min-content;
      grid-auto-rows: auto;
      gap: 5px;
      padding: 4px;
      background-color: $region-color;
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
export class Tooltip {
  readonly #store = inject(GameStore);
  readonly landmarkKey = input.required<number>();
  readonly landmark = computed(() => this.#store.defs().allRegions[this.landmarkKey()] as AutopelagoLandmarkRegion);
  readonly landmarkLocation = computed(() => this.#store.defs().allLocations[this.landmark().loc]);
}
