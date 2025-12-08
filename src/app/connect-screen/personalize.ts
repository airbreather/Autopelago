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
import { TinyColor } from '@ctrl/tinycolor';
import { ColorPicker } from '../color-picker/color-picker';
import { applyPixelColors, getPixelTones, type PixelTones } from '../utils/color-helpers';
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
        <div></div>
        <button class="no-color-change-on-press" [class.hovering]="hoveringPlayer1()" [class.selected]="selectedPlayer1()" (click)="select('player1')" (mouseenter)="hover('player1')" (mouseleave)="hover(null)">
          <canvas #player1Canvas width="64" height="64"></canvas>
        </button>
        <button class="no-color-change-on-press" [class.hovering]="hoveringPlayer2()" [class.selected]="selectedPlayer2()" (click)="select('player2')" (mouseenter)="hover('player2')" (mouseleave)="hover(null)">
          <canvas #player2Canvas width="64" height="64"></canvas>
        </button>
        <button class="no-color-change-on-press" [class.hovering]="hoveringPlayer4()" [class.selected]="selectedPlayer4()" (click)="select('player4')" (mouseenter)="hover('player4')" (mouseleave)="hover(null)">
          <canvas #player4Canvas width="64" height="64"></canvas>
        </button>
        <div></div>
      </div>
      <app-color-picker class="color-picker" [(color)]="selectedColor"/>
    </div>
  `,
  styles: `
    @use '../../theme';

    .outer {
      height: 80vh;
      width: 400px;
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
      width: 100%;
      button {
        border: 3px solid black;
      }
    }

    .color-picker {
      flex: 1;
      width: 100%;
    }

    .hovering {
      canvas {
        scale: 0.75;
      }
    }

    .selected {
      background-color: theme.$accent-dark-2;
      &:hover {
        background-color: theme.$accent-dark-1;
      }
      canvas {
        scale: 1;
      }
    }

    canvas {
      image-rendering: pixelated;
      filter: drop-shadow(3px 3px 2px black);
      scale: 0.5;
      transition: scale 0.1s ease-in-out;
      animation: wiggle 1s linear infinite;
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

  readonly #selected = signal<'player1' | 'player2' | 'player4'>('player1');
  protected readonly selectedPlayer1 = computed(() => this.#selected() === 'player1');
  protected readonly selectedPlayer2 = computed(() => this.#selected() === 'player2');
  protected readonly selectedPlayer4 = computed(() => this.#selected() === 'player4');
  readonly #hoveringOver = signal<'player1' | 'player2' | 'player4' | null>(null);
  protected readonly hoveringPlayer1 = computed(() => this.#hoveringOver() === 'player1' && !this.selectedPlayer1());
  protected readonly hoveringPlayer2 = computed(() => this.#hoveringOver() === 'player2' && !this.selectedPlayer2());
  protected readonly hoveringPlayer4 = computed(() => this.#hoveringOver() === 'player4' && !this.selectedPlayer4());

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

      applyPixelColors(this.selectedColor(), pixelTones.player1);
      player1.putImageData(pixelTones.player1.data, 0, 0);

      applyPixelColors(this.selectedColor(), pixelTones.player2);
      player2.putImageData(pixelTones.player2.data, 0, 0);

      applyPixelColors(this.selectedColor(), pixelTones.player4);
      player4.putImageData(pixelTones.player4.data, 0, 0);
    });
  }

  protected hover(player: 'player1' | 'player2' | 'player4' | null) {
    this.#hoveringOver.set(player);
  }

  protected select(player: 'player1' | 'player2' | 'player4') {
    this.#selected.set(player);
  }
}
