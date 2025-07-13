import { Component, computed, input } from '@angular/core';

import { Landmark, LANDMARKS } from "../../../../data/locations";

@Component({
  selector: 'app-landmark-markers',
  imports: [],
  template: `
    @for (landmarks of landmarks(); track landmarks.key) {
      <!--suppress AngularNgOptimizedImage -->
      <img class="quest-marker"
           [alt]="landmarks.key"
           [src]="qUrls()[(landmarks.value.sprite_index + 1) % 2]"
           [style.left]="'calc(100% * ' + landmarks.calcQLeft + ' / 300)'"
           [style.top]="'calc(100% * ' + landmarks.calcQTop + ' / 450)'" />

      <!--suppress AngularNgOptimizedImage -->
      <img class="landmark"
           [class.unchecked]="landmarks.value.sprite_index % 2 === 0"
           [alt]="landmarks.key"
           [src]="landmarks.url"
           [style.left]="'calc(100% * ' + landmarks.calcLeft + ' / 300)'"
           [style.top]="'calc(100% * ' + landmarks.calcTop + ' / 450)'" />
    }
  `,
  styles: `
    .quest-marker {
      position: absolute;
      width: calc(100% * 12 / 300);
      height: calc(100% * 12 / 450);
    }

    .landmark {
      position: absolute;
      width: calc(100% * 16 / 300);
      height: calc(100% * 16 / 450);

      &.unchecked {
        filter: drop-shadow(8px 16px 16px black) saturate(10%) brightness(40%);
      }

      &:not(.unchecked) {
        filter: drop-shadow(8px 16px 16px black);
      }
    }
  `,
})
export class LandmarkMarkers {
  readonly pathBase = input.required<string | null>();

  readonly landmarks = computed(() => {
    const pathBase = (this.pathBase() ?? '') + '/assets/images/locations/';
    return Object.entries(LANDMARKS).map(([k, v]) => ({
      key: k,
      value: v as Landmark,
      url: pathBase + k + '.webp',
      calcLeft: (v.coords[0] - 8).toString(),
      calcTop: (v.coords[1] - 8).toString(),
      calcQLeft: (v.coords[0] - 6).toString(),
      calcQTop: (v.coords[1] - 21).toString(),
    }));
  });

  readonly qUrls = computed(() =>  {
    const pathBase = (this.pathBase() ?? '') + '/assets/images/locations/';
    return [pathBase + 'yellow_quest.webp', pathBase + 'gray_quest.webp'];
  });
}
