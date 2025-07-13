import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, UrlSegment } from "@angular/router";
import { rxResource } from "@angular/core/rxjs-interop";
import { map } from "rxjs/operators";

@Component({
  selector: 'app-game-tab-map',
  imports: [],
  template: `
    <div>
      <!--suppress AngularNgOptimizedImage -->
      <img alt="map" [src]="mapSrc()" />
    </div>
  `,
  styles: ``,
})
export class GameTabMap {
  readonly #route = inject(ActivatedRoute);
  pathBase = rxResource<UrlSegment | null, never>({
    defaultValue: null,
    stream: () => this.#route.root.url.pipe(map(u => u[0])),
  });
  mapSrc = computed(() => {
    const pathBase = this.pathBase.value();
    return pathBase ? pathBase.path + '/assets/map.svg' : null;
  });
}
