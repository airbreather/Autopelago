import { AfterViewInit, Component, DestroyRef, ElementRef, inject, viewChild } from '@angular/core';

import { FillerMarkers } from './filler-markers/filler-markers';
import { LandmarkMarkers } from './landmark-markers/landmark-markers';
import { PauseButton } from './pause-button/pause-button';
import { PlayerToken } from './player-token/player-token';
import { GameStoreService } from '../../../store/autopelago-store';

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
export class GameTabMap implements AfterViewInit {
  protected readonly pixiCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('pixiCanvas');
  protected readonly outerDiv = viewChild.required<ElementRef<HTMLDivElement>>('outer');

  readonly #gameStore = inject(GameStoreService);
  readonly #destroy = inject(DestroyRef);

  ngAfterViewInit() {
    const canvas = this.pixiCanvas().nativeElement;
    const outerDiv = this.outerDiv().nativeElement;
    void this.#gameStore.initInterface(canvas, outerDiv, this.#destroy);
  }
}
