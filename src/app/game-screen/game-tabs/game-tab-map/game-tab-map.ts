import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, UrlSegment } from "@angular/router";
import { rxResource } from "@angular/core/rxjs-interop";
import { map } from "rxjs/operators";
import { Location, LOCATIONS } from "../../../data/locations";
import { NgStyle } from "@angular/common";

@Component({
  selector: 'app-game-tab-map',
  imports: [NgStyle],
  template: `
    <div class="outer">
      <!--suppress AngularNgOptimizedImage -->
      <img alt="map" [src]="mapSrc()" />
      @for (location of getLocations(); track location[0]) {
        <img class="quest-marker" alt="location.sprite_index" [src]="getQURLs()[(location[1].sprite_index + 1) % 2]" [ngStyle]="computeQStyle(location[1])" />
        <img class="landmark" [class.unchecked]="location[1].sprite_index % 2 === 0" alt="location.sprite_index" [src]="location[2]" [ngStyle]="computeLocationStyle(location[1])" />
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

  getQURLs(): readonly [string, string] {
    const pathBase = this.pathBase.value();
    const pathBase2 = pathBase ? pathBase.path + '/assets/images/locations/' : '';
    return [pathBase2 + 'yellow_quest.webp', pathBase2 + 'gray_quest.webp'];
  }

  getLocations() {
    const pathBase = this.pathBase.value();
    const pathBase2 = pathBase ? pathBase.path + '/assets/images/locations/' : '';
    return Object.entries(LOCATIONS).map(([k, v]) => [k, v as Location, pathBase2 + k + '.webp'] as const);
  }

  computeQStyle(l: Location): Partial<CSSStyleDeclaration> {
    return {
      left: `calc(100% * ${(l.coords[0] - 8 + 2).toString()} / 300)`,
      top: `calc(100% * ${(l.coords[1] - 8 - 13).toString()} / 450)`,
    };
  }

  computeLocationStyle(l: Location): Partial<CSSStyleDeclaration> {
    return {
      left: `calc(100% * ${(l.coords[0] - 8).toString()} / 300)`,
      top: `calc(100% * ${(l.coords[1] - 8).toString()} / 450)`,
    };
  }
}
