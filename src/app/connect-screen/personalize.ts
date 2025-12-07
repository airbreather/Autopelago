import { ChangeDetectionStrategy, Component, computed, type ElementRef, viewChild } from '@angular/core';
import { ColorSketchModule } from 'ngx-color/sketch';
import { elementSizeSignal } from '../utils/element-size';
import { resizeText } from '../utils/resize-text';

@Component({
  selector: 'app-personalize',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ColorSketchModule,
  ],
  template: `
    <div #outer class="outer">
      <h1 #header class="header">Personalize Your Rat!</h1>
      <div class="rat-options">
        <button>
          <img alt="normal rat" src="/assets/images/players/pack_rat.webp">
        </button>
      </div>
      <color-sketch class="color-picker" [disableAlpha]="true" [width]="outerWidth()"></color-sketch>
    </div>
  `,
  styles: `
    @use '../../theme';

    .outer {
      height: 80vh;
      width: 80vw;
      min-width: 400px;
      min-height: 600px;
      display: flex;
      flex-direction: column;
      gap: 40px;
      background-color: theme.$region-color;
      align-items: center;
      justify-content: center;
      border: 1px solid black;
    }

    .header {
      flex: 0;
      white-space: nowrap;
      padding-left: 40px;
      padding-right: 40px;
    }

    .rat-options {
      flex: 0;
      display: flex;
      justify-content: space-between;
      height: 64px;
    }

    .color-picker {
      flex: 1;
      ::ng-deep * {
        color: revert-layer;
      }
    }
  `,
})
export class Personalize {
  protected readonly outer = viewChild.required<ElementRef<HTMLDivElement>>('outer');
  protected readonly header = viewChild.required<ElementRef<HTMLHeadingElement>>('header');
  readonly #outerSize = elementSizeSignal(this.outer);
  protected readonly outerWidth = computed(() => {
    const { clientWidth, clientHeight } = this.#outerSize();
    if (clientWidth > 800 && clientHeight > 940) {
      return 720;
    }

    const maxWidth = clientWidth - 80;
    if (clientHeight > 940) {
      return maxWidth;
    }

    return Math.min(maxWidth, (clientHeight * 1.4) - 600);
  });

  constructor() {
    resizeText({
      outer: this.outer,
      inner: this.header,
      max: 40,
      text: computed(() => 'Personalize Your Rat!'),
    });
  }
}
