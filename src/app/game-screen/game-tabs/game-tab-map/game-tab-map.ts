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
        <img alt="location.sprite_index" [src]="location[2]" [ngStyle]="computeLocationStyle(location[1])" />
      }
    </div>
  `,
  styles: `
    .outer {
      position: relative;
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
  locationsSrc = computed(() => {
    const pathBase = this.pathBase.value();
    return pathBase ? pathBase.path + '/assets/images/locations/' : null;
  });

  getLocations() {
    const pathBase = this.pathBase.value();
    const pathBase2 = pathBase ? pathBase.path + '/assets/images/locations/' : '';
    return Object.entries(LOCATIONS).map(([k, v]) => [k, v as Location, pathBase2 + k + '.webp'] as const);
  }

  computeLocationStyle(l: Location): Partial<CSSStyleDeclaration> {
    const pathBase = this.pathBase.value();
    if (!pathBase) {
      return { };
    }

    return {
      position: 'absolute',
      left: `calc(100% * ${(l.coords[0] - 8).toString()} / 300)`,
      top: `calc(100% * ${(l.coords[1] - 8).toString()} / 450)`,
      width: `calc(100% * 16 / 300)`,
      height: `calc(100% * 16 / 450)`,
    };
  }
}
