import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  type ElementRef,
  resource,
  signal,
  viewChild,
} from '@angular/core';
import { TinyColor } from '@ctrl/tinycolor/dist';
import { ColorPicker } from '../color-picker/color-picker';
import { applyPixelColors, getPixelTones, type PixelTones, toRatDark } from '../utils/color-helpers';
import { resizeText } from '../utils/resize-text';

interface PlayerImages {
  player1Image: HTMLImageElement;
  player2Image: HTMLImageElement;
  player4Image: HTMLImageElement;
}

interface PlayerPixelTones {
  player1: PixelTones;
  player2: PixelTones;
  player4: PixelTones;
}

@Component({
  selector: 'app-personalize',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ColorPicker,
  ],
  template: `
    <img #player1 alt="player1" src="/assets/images/players/pack_rat.webp" hidden>
    <img #player2 alt="player2" src="/assets/images/players/player2.webp" hidden>
    <img #player4 alt="player4" src="/assets/images/players/player4.webp" hidden>
    <div #outer class="outer">
      <h1 #header class="header">Personalize Your Rat!</h1>
      <div class="rat-options">
        <button>
          <canvas #player1Canvas width="64" height="64"></canvas>
        </button>
        <button>
          <canvas #player2Canvas width="64" height="64"></canvas>
        </button>
        <button>
          <canvas #player4Canvas width="64" height="64"></canvas>
        </button>
      </div>
      <app-color-picker class="color-picker" [(color)]="selectedColor"/>
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
    }

    canvas {
      image-rendering: pixelated;
    }
  `,
})
export class Personalize {
  protected readonly player1 = viewChild.required<ElementRef<HTMLImageElement>>('player1');
  protected readonly player2 = viewChild.required<ElementRef<HTMLImageElement>>('player2');
  protected readonly player4 = viewChild.required<ElementRef<HTMLImageElement>>('player4');

  protected readonly outer = viewChild.required<ElementRef<HTMLDivElement>>('outer');
  protected readonly header = viewChild.required<ElementRef<HTMLHeadingElement>>('header');
  protected readonly selectedColor = signal(new TinyColor('#382E26'));

  protected readonly player1Canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('player1Canvas');
  protected readonly player2Canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('player2Canvas');
  protected readonly player4Canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('player4Canvas');
  readonly #player1CanvasContext = computed(() => this.player1Canvas().nativeElement.getContext('2d'));
  readonly #player2CanvasContext = computed(() => this.player2Canvas().nativeElement.getContext('2d'));
  readonly #player4CanvasContext = computed(() => this.player4Canvas().nativeElement.getContext('2d'));

  constructor() {
    resizeText({
      outer: this.outer,
      inner: this.header,
      max: 40,
      text: computed(() => 'Personalize Your Rat!'),
    });

    const img = resource({
      defaultValue: null,
      params: () => ({ player1: this.player1(), player2: this.player2(), player4: this.player4() }),
      loader: async ({ params: { player1, player2, player4 } }) => {
        await Promise.all([player1, player2, player4].map(i => i.nativeElement.decode()));
        return {
          player1Image: player1.nativeElement,
          player2Image: player2.nativeElement,
          player4Image: player4.nativeElement,
        } as PlayerImages | null;
      },
    });
    const ratPixelTones = signal<PlayerPixelTones | null>(null);
    effect(() => {
      const playerImages = img.value();
      const player1Ctx = this.#player1CanvasContext();
      const player2Ctx = this.#player2CanvasContext();
      const player4Ctx = this.#player4CanvasContext();
      if (playerImages === null || player1Ctx === null || player2Ctx === null || player4Ctx === null) {
        return;
      }

      ratPixelTones.set({
        player1: getPixelTones(playerImages.player1Image, player1Ctx),
        player2: getPixelTones(playerImages.player2Image, player2Ctx),
        player4: getPixelTones(playerImages.player4Image, player4Ctx),
      });
    });
    effect(() => {
      const pixelTones = ratPixelTones();
      if (pixelTones === null) {
        return;
      }

      const player1 = this.#player1CanvasContext();
      const player2 = this.#player2CanvasContext();
      const player4 = this.#player4CanvasContext();
      if (player1 === null || player2 === null || player4 === null) {
        return;
      }

      const lightColor = this.selectedColor();
      const darkColor = toRatDark(lightColor);
      applyPixelColors(
        player1,
        { color: lightColor, pixels: pixelTones.player1.light },
        { color: darkColor, pixels: pixelTones.player1.dark },
      );
      applyPixelColors(
        player2,
        { color: lightColor, pixels: pixelTones.player2.light },
        { color: darkColor, pixels: pixelTones.player2.dark },
      );
      applyPixelColors(
        player4,
        { color: lightColor, pixels: pixelTones.player4.light },
        { color: darkColor, pixels: pixelTones.player4.dark },
      );
    });
  }
}
