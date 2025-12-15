import { Component, computed, effect, type ElementRef, inject, viewChild } from '@angular/core';
import { GameStore } from '../../../../store/autopelago-store';

const RAT_THOUGHTS_LACTOSE = [
  'squeak squeak',
  'the moon looks cheesy today',
  'i could really go for some cheddar',
  'wait, is there even air in space?',
  'i sure hope the moon hasn\'t gotten moldy',
  'wait... you can read my mind?',
  'did you know, real rats don\'t even like cheese all that much!',
  'don\'t you DARE call me a mouse!',
  'rat rat rat rat rat rat rat rat rat rat rat rat',
  'i may live in a sewer, but i\'m squeaky clean!',
  'ahem, a little privacy please?',
  'you\'re not a cat, are you? just checking...',
  '\'click me to see where I want to go\'? what does that mean?',
];

const RAT_THOUGHTS_LACTOSE_INTOLERANT = [
  'squeak squeak',
  'the moon looks spidery today',
  'i could really go for some spiders',
  'wait, is there even air in space?',
  'i sure hope the moon hasn\'t gotten moldy',
  'wait... you can read my mind?',
  'did you know, real rats don\'t even like spiders all that much!',
  'don\'t you DARE call me a mouse!',
  'rat rat rat rat rat rat rat rat rat rat rat rat',
  'i may live in a sewer, but i\'m squeaky clean!',
  'ahem, a little privacy please?',
  'you\'re not a cat, are you? just checking...',
  '\'click me to see where I want to go\'? what does that mean?',
];

@Component({
  selector: 'app-player-tooltip',
  imports: [],
  template: `
    <div class="outer">
      <h1 class="box header">Rat Thoughts</h1>
      <div class="box main-content">
        <canvas #playerToken class="player-image" width="64" height="64"></canvas>
        <div class="current-location">●At '{{currentLocationName()}}'</div>
        <div class="target-location">●Going to '{{targetLocationName()}}'</div>
        <div class="rat-thought">{{ratThought()}}</div>
      </div>
    </div>
  `,
  styles: `
    @use '../../../../../theme';

    .outer {
      display: grid;
      grid-template-columns: min-content;
      grid-auto-rows: auto;
      gap: 5px;
      padding: 4px;
      background-color: theme.$region-color;
    }

    .box {
      padding: 4px;
      border: 2px solid black;
    }

    .header {
      margin: 0;
      font-size: 14px;
      white-space: nowrap;
    }

    .main-content {
      display: grid;
      grid-template-columns: auto min-content;
      grid-template-rows: repeat(3, auto);
      align-items: start;
    }

    .player-image {
      grid-row: 1 / span 3;
      grid-column: 1;
    }

    .current-location {
      grid-row: 1;
      grid-column: 2;
      margin-left: 5px;
      font-size: 14px;
      white-space: nowrap;
    }

    .target-location {
      grid-row: 2;
      grid-column: 2;
      margin-left: 5px;
      font-size: 14px;
      white-space: nowrap;
    }

    .rat-thought {
      grid-row: 3;
      grid-column: 2;
      margin-left: 5px;
      font-size: 8pt;
      margin-top: 10px;
    }
  `,
})
export class PlayerTooltip {
  readonly #store = inject(GameStore);

  protected readonly playerToken = viewChild.required<ElementRef<HTMLCanvasElement>>('playerToken');
  constructor() {
    effect(() => {
      const playerToken = this.#store.playerTokenValue();
      if (playerToken === null) {
        return;
      }

      const canvas = this.playerToken().nativeElement;
      const ctx = canvas.getContext('2d');
      if (ctx === null) {
        return;
      }

      ctx.putImageData(playerToken.data, 0, 0);
    });
  }

  protected readonly currentLocationName = computed(() => {
    const { allLocations } = this.#store.defs();
    return allLocations[this.#store.currentLocation()].name;
  });

  protected readonly targetLocationName = computed(() => {
    const { allLocations } = this.#store.defs();
    return allLocations[this.#store.targetLocation()].name;
  });

  protected readonly ratThought = computed(() => {
    const ratThoughts = this.#store.lactoseIntolerant() ? RAT_THOUGHTS_LACTOSE_INTOLERANT : RAT_THOUGHTS_LACTOSE;
    return ratThoughts[Math.floor(Math.random() * ratThoughts.length)];
  });
}
