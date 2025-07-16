import { AfterViewInit, Component, ElementRef, inject, viewChild } from '@angular/core';

import { PauseButton } from "./pause-button/pause-button";
import { PixiService } from "./pixi-service";
import { LandmarkMarkers } from "./landmark-markers/landmark-markers";

@Component({
  selector: 'app-game-tab-map',
  imports: [PauseButton, LandmarkMarkers],
  providers: [PixiService],
  template: `
    <div #outer class="outer">
      <!--suppress AngularNgOptimizedImage -->
      <img alt="map" src="/assets/images/map.svg" />
      <canvas #pixiCanvas class="pixi-canvas" width="300" height="450">
      </canvas>
      <div #pauseButtonContainer class="pause-button-container" [style.margin-top]="'-' + pauseButtonContainer.clientHeight + 'px'">
        <app-pause-button />
      </div>
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

  readonly #pixiService = inject(PixiService, { self: true });

  ngAfterViewInit() {
    const canvas = this.pixiCanvas().nativeElement;
    const outerDiv = this.outerDiv().nativeElement;
    void this.#pixiService.init(canvas, outerDiv);
  }
}
