import { Component, computed, inject, viewChild } from '@angular/core';
import { rxResource, takeUntilDestroyed, toObservable } from "@angular/core/rxjs-interop";
import { ActivatedRoute, UrlSegment } from "@angular/router";

import { SplitAreaComponent, SplitComponent } from "angular-split";

import { map, mergeMap } from "rxjs/operators";

import { GameScreenStore } from "../store/game-screen-store";

import { StatusDisplay } from "./status-display/status-display";

@Component({
  selector: 'app-game-screen',
  imports: [
    SplitComponent, SplitAreaComponent, StatusDisplay,
  ],
  template: `
    <as-split #outer class="outer" unit="percent" direction="horizontal"
              gutterDblClickDuration="500" (gutterDblClick)="onGutterDblClick()">
      <as-split-area class="left" [size]="leftSize()" [minSize]="5" [maxSize]="95">
        <app-status-display />
      </as-split-area>
      <as-split-area class="right">
        <div class="top">
          <!--suppress AngularNgOptimizedImage -->
          <img alt="map" [src]="mapSrc()" />
        </div>
        <div class="bottom">
          <div class="tab-map">
            Map
          </div>
          <div class="tab-arcade">
            Arcade
          </div>
          <div class="tab-filler">
          </div>
        </div>
      </as-split-area>
    </as-split>
  `,
  styles: `
    .outer {
      width: 100vw;
      height: 100vh;
    }

    .left {
      display: flex;
      flex-direction: column;
    }

    .right {
      display: flex;
      flex-direction: column;
      height: 100%;

      .top {
        flex: 1;
        overflow: auto;
      }

      .bottom {
        flex: 0;
        display: flex;

        .tab-map {
          flex: 0;
        }

        .tab-arcade {
          flex: 0;
        }

        .tab-filler {
          flex: 1;
        }
      }
    }
  `,
})
export class GameScreen {
  readonly #store = inject(GameScreenStore);
  readonly #route = inject(ActivatedRoute);
  protected readonly splitRef = viewChild.required<SplitComponent>('outer');

  pathBase = rxResource<UrlSegment | null, never>({
    defaultValue: null,
    stream: () => this.#route.root.url.pipe(map(u => u[0])),
  });
  mapSrc = computed(() => {
    const pathBase = this.pathBase.value();
    return pathBase ? pathBase.path + '/assets/map.svg' : null;
  });

  leftSize = this.#store.leftSize;

  constructor() {
    toObservable(this.splitRef).pipe(
      mergeMap(split => split.dragProgress$),
      takeUntilDestroyed(),
    ).subscribe(evt => {
      this.#store.updateLeftSize(evt.sizes[0] as number);
    });
  }

  onGutterDblClick() {
    this.#store.restoreDefaultLeftSize();
  }
}
