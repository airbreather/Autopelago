import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, UrlSegment } from "@angular/router";

import { rxResource } from "@angular/core/rxjs-interop";
import { map } from "rxjs";
import { LandmarkMarkers } from "./landmark-markers/landmark-markers";

@Component({
  selector: 'app-game-tab-map',
  imports: [
    LandmarkMarkers
  ],
  template: `
    <div class="outer">
      <!--suppress AngularNgOptimizedImage -->
      <img alt="map" [src]="mapSrc()" />
      <app-landmark-markers [pathBase]="pathBase.value()?.path ?? null" />
    </div>
  `,
  styles: `
    .outer {
      position: relative;
      pointer-events: none;
      user-select: none;
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
}
