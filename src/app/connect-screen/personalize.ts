import { DIALOG_DATA } from '@angular/cdk/dialog';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  type ElementRef,
  inject,
  type Signal,
  signal,
  viewChild,
  type WritableSignal,
} from '@angular/core';
import { type ColorInput, TinyColor } from '@ctrl/tinycolor';
import { ColorPicker } from '../color-picker/color-picker';
import { applyPixelColors, getPixelTones, type PixelTones } from '../utils/color-helpers';
import { resizeText } from '../utils/resize-text';
import { CONNECT_SCREEN_STATE_DEFAULTS, type PlayerIcon } from './connect-screen-state';

export interface PersonalizeData {
  readonly playerImages: Signal<PlayerImages | null>;
  readonly playerIcon: WritableSignal<PlayerIcon>;
  readonly playerColor: WritableSignal<ColorInput>;
  readonly canvas: CanvasRenderingContext2D;
}

export interface PlayerImages {
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
    <div #outer class="outer">
      <h1 #header class="header">Personalize Your Rat!</h1>
      <div class="rat-options">
        <div></div>
        <button class="no-color-change-on-press" [class.selected]="selectedPlayer1()" (click)="select(1)">
          <canvas #player1Canvas width="64" height="64"></canvas>
        </button>
        <button class="no-color-change-on-press" [class.selected]="selectedPlayer2()" (click)="select(2)">
          <canvas #player2Canvas width="64" height="64"></canvas>
        </button>
        <button class="no-color-change-on-press" [class.selected]="selectedPlayer4()" (click)="select(4)">
          <canvas #player4Canvas width="64" height="64"></canvas>
        </button>
        <div></div>
      </div>
      <button class="reset-to-default" (click)="resetToDefault()">
        Reset to Default
      </button>
      <app-color-picker class="color-picker" [(color)]="selectedColor" />
    </div>
  `,
  styles: `
    @use '../../theme';

    .outer {
      height: 80vh;
      width: 400px;
      display: flex;
      flex-direction: column;
      gap: 20px;
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
      width: 100%;
      button {
        border: 3px solid black;
        canvas {
          image-rendering: pixelated;
          filter: drop-shadow(3px 3px 2px black);
          animation: wiggle 1s linear infinite;
          scale: 0.5;
          transition: scale 0.1s ease-in;
        }
        &:hover:not(.selected) {
          canvas {
            scale: 0.75;
          }
        }
        &.selected {
          background-color: theme.$accent-dark-2;
          &:hover {
            background-color: theme.$accent-dark-1;
          }
          canvas {
            scale: 1;
          }
        }
      }
    }

    .reset-to-default {
      flex: 0;
    }

    .color-picker {
      flex: 1;
      width: 100%;
      padding: 5px;
    }

    @keyframes wiggle {
      0% {
        transform: rotate(0);
      }
      25% {
        transform: rotate(10deg);
      }
      50% {
        transform: rotate(0);
      }
      75% {
        transform: rotate(-10deg);
      }
      100% {
        transform: rotate(0);
      }
    }
  `,
})
export class Personalize {
  readonly #data = inject<PersonalizeData>(DIALOG_DATA);

  protected readonly outer = viewChild.required<ElementRef<HTMLDivElement>>('outer');
  protected readonly header = viewChild.required<ElementRef<HTMLHeadingElement>>('header');

  protected readonly selectedColor = this.#data.playerColor;

  protected readonly player1Canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('player1Canvas');
  protected readonly player2Canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('player2Canvas');
  protected readonly player4Canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('player4Canvas');
  readonly #player1CanvasContext = computed(() => this.player1Canvas().nativeElement.getContext('2d'));
  readonly #player2CanvasContext = computed(() => this.player2Canvas().nativeElement.getContext('2d'));
  readonly #player4CanvasContext = computed(() => this.player4Canvas().nativeElement.getContext('2d'));

  protected readonly selectedPlayer1 = computed(() => this.#data.playerIcon() === 1);
  protected readonly selectedPlayer2 = computed(() => this.#data.playerIcon() === 2);
  protected readonly selectedPlayer4 = computed(() => this.#data.playerIcon() === 4);

  constructor() {
    resizeText({
      outer: this.outer,
      inner: this.header,
      max: 40,
      text: computed(() => 'Personalize Your Rat!'),
    });

    const ratPixelTones = signal<PlayerPixelTones | null>(null);
    effect(() => {
      const playerImages = this.#data.playerImages();
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

      const color = new TinyColor(this.selectedColor());
      applyPixelColors(color, pixelTones.player1);
      player1.putImageData(pixelTones.player1.data, 0, 0);

      applyPixelColors(color, pixelTones.player2);
      player2.putImageData(pixelTones.player2.data, 0, 0);

      applyPixelColors(color, pixelTones.player4);
      player4.putImageData(pixelTones.player4.data, 0, 0);

      const playerIconString = this.#data.playerIcon().toString() as `${PlayerIcon}`;
      this.#data.canvas.putImageData(pixelTones[`player${playerIconString}`].data, 0, 0);
    });
  }

  protected select(playerIcon: PlayerIcon) {
    this.#data.playerIcon.set(playerIcon);
  }

  protected resetToDefault() {
    this.#data.playerIcon.set(CONNECT_SCREEN_STATE_DEFAULTS.playerIcon);
    this.#data.playerColor.set(CONNECT_SCREEN_STATE_DEFAULTS.playerColor);
  }
}
