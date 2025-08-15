import { Component, DestroyRef, effect, ElementRef, inject, viewChild } from '@angular/core';

import { PixiPlugins } from '../../../pixi-plugins';
import { FillerMarkers } from './filler-markers/filler-markers';
import { LandmarkMarkers } from './landmark-markers/landmark-markers';
import { PauseButton } from './pause-button/pause-button';
import { PlayerToken } from './player-token/player-token';

@Component({
  selector: 'app-game-tab-map',
  imports: [PauseButton, LandmarkMarkers, PlayerToken, FillerMarkers],
  template: `
    <div #outer class="outer">
      <!--suppress AngularNgOptimizedImage, HtmlUnknownTarget -->
      <img alt="map" src="assets/images/map.svg" />
      <canvas #pixiCanvas class="pixi-canvas" width="300" height="450">
      </canvas>
      <app-player-token />
      <div #pauseButtonContainer class="pause-button-container" [style.margin-top]="'-' + pauseButtonContainer.clientHeight + 'px'">
        <app-pause-button />
      </div>
      <app-filler-markers />
      <app-landmark-markers />
    </div>
  `,
  styles: `
    .outer {
      position: relative;
      pointer-events: none;
      user-select: none;
    }

    .pause-button-container {
      position: sticky;
      margin-bottom: 0;
      left: 5px;
      bottom: 5px;
      pointer-events: initial;
      width: fit-content;
    }

    .pixi-canvas {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
    }
  `,
})
export class GameTabMap {
  protected readonly pixiCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('pixiCanvas');
  protected readonly outerDiv = viewChild.required<ElementRef<HTMLDivElement>>('outer');

  constructor() {
    const destroyRef = inject(DestroyRef);
    const pixiPlugins = inject(PixiPlugins);
    effect(() => {
      const canvas = this.pixiCanvas().nativeElement;
      const outerDiv = this.outerDiv().nativeElement;
      void pixiPlugins.initInterface(canvas, outerDiv, destroyRef);
    });
  }
}
