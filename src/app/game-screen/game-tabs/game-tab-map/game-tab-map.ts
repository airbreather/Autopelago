import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, UrlSegment } from "@angular/router";

import { rxResource } from "@angular/core/rxjs-interop";
import { map } from "rxjs";

import { Landmark, LANDMARKS } from "../../../data/locations";

@Component({
  selector: 'app-game-tab-map',
  imports: [],
  template: `
    <div class="outer">
      <!--suppress AngularNgOptimizedImage -->
      <img alt="map" [src]="mapSrc()" />
      @for (landmarks of landmarks(); track landmarks.key) {
        <img class="quest-marker"
             [alt]="landmarks.key"
             [src]="qUrls()[(landmarks.value.sprite_index + 1) % 2]"
             [style.left]="'calc(100% * ' + landmarks.calcQLeft + ' / 300)'"
             [style.top]="'calc(100% * ' + landmarks.calcQTop + ' / 450)'" />

        <img class="landmark"
             [class.unchecked]="landmarks.value.sprite_index % 2 === 0" 
             [alt]="landmarks.key"
             [src]="landmarks.url"
             [style.left]="'calc(100% * ' + landmarks.calcLeft + ' / 300)'"
             [style.top]="'calc(100% * ' + landmarks.calcTop + ' / 450)'" />
      }
    </div>
  `,
  styles: `
    .outer {
      position: relative;
      pointer-events: none;
      user-select: none;
    }

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
export class GameTabMap {
  readonly #route = inject(ActivatedRoute);
  pathBase = rxResource<UrlSegment | null, never>({
    defaultValue: null,
    stream: () => this.#route.root.url.pipe(map(u => u[0])),
  });
  mapSrc = computed(() => {
    const pathBase = this.pathBase.value();
    return pathBase ? pathBase.path + '/assets/images/map.svg' : null;
  });

  readonly landmarks = computed(() => {
    const pathBase = this.pathBase.value();
    const pathBase2 = pathBase ? pathBase.path + '/assets/images/locations/' : '';
    return Object.entries(LANDMARKS).map(([k, v]) => ({
      key: k,
      value: v as Landmark,
      url: pathBase2 + k + '.webp',
      calcLeft: (v.coords[0] - 8).toString(),
      calcTop: (v.coords[1] - 8).toString(),
      calcQLeft: (v.coords[0] - 6).toString(),
      calcQTop: (v.coords[1] - 21).toString(),
    }));
  });

  readonly qUrls = computed(() =>  {
    const pathBase = this.pathBase.value();
    const pathBase2 = pathBase ? pathBase.path + '/assets/images/locations/' : '';
    return [pathBase2 + 'yellow_quest.webp', pathBase2 + 'gray_quest.webp'];
  });
}
