import { AfterViewInit, Component, ElementRef, inject, viewChild } from '@angular/core';

import { PauseButton } from "./pause-button/pause-button";
import { PixiService } from "./pixi-service";
import { LandmarkMarkers } from "./landmark-markers/landmark-markers";

@Component({
  selector: 'app-game-tab-map',
  imports: [PauseButton, LandmarkMarkers],
  providers: [PixiService],
  template: `
    <div #theOuter class="outer">
      <!--suppress AngularNgOptimizedImage -->
      <img alt="map" src="/assets/images/map.svg" />
      <app-pause-button />
      <canvas #theCanvas class="the-canvas" width="300" height="450">
      </canvas>
    </div>
    <app-landmark-markers />
  `,
  styles: `
    .outer {
      position: relative;
      pointer-events: none;
      user-select: none;
    }
    .the-canvas {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
    }
  `,
})
export class GameTabMap implements AfterViewInit {
  protected readonly theCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('theCanvas');
  protected readonly theOuter = viewChild.required<ElementRef<HTMLDivElement>>('theOuter');

  readonly #pixiService = inject(PixiService, { self: true });

  ngAfterViewInit() {
    const canvas = this.theCanvas().nativeElement;
    const outer = this.theOuter().nativeElement;
    void this.#pixiService.init(canvas, outer);
  }
}
