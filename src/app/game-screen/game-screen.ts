import { Component, computed, ElementRef, inject, input, viewChild } from '@angular/core';
import { rxResource, takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';

import { SplitAreaComponent, SplitComponent } from 'angular-split';

import { map, mergeMap } from 'rxjs';
import type { AutopelagoClientAndData } from '../data/slot-data';

import { GameScreenStore } from '../store/game-screen-store';
import { resizeEvents } from '../util';
import { GameTabs } from './game-tabs/game-tabs';
import { StatusDisplay } from './status-display/status-display';

@Component({
  selector: 'app-game-screen',
  imports: [
    SplitComponent, SplitAreaComponent, StatusDisplay, GameTabs,
  ],
  template: `
    <div #outer class="outer">
      <as-split #split unit="pixel" direction="horizontal"
                gutterDblClickDuration="500" (gutterDblClick)="onGutterDblClick()">
        <as-split-area class="left" [size]="leftSize()" [minSize]="minSize()" [maxSize]="maxSize()">
          <app-status-display />
        </as-split-area>
        <as-split-area class="right">
          <app-game-tabs [game]="game()" />
        </as-split-area>
      </as-split>
    </div>
  `,
  styles: `
    .outer {
      width: 100vw;
      height: 100vh;
    }
  `,
})
export class GameScreen {
  readonly #store = inject(GameScreenStore);
  readonly game = input.required<AutopelagoClientAndData>();
  protected readonly splitRef = viewChild.required<SplitComponent>('split');
  protected readonly outerRef = viewChild.required<ElementRef<HTMLDivElement>>('outer');

  readonly #width = rxResource<number | null, ElementRef<HTMLDivElement>>({
    defaultValue: null,
    params: () => this.outerRef(),
    stream: ({ params: outerRef }) => {
      return resizeEvents(outerRef.nativeElement).pipe(
        map(() => outerRef.nativeElement.scrollWidth),
      );
    },
  });

  readonly minSize = computed(() => {
    const width = this.#width.value();
    if (!width) {
      return 100;
    }

    return Math.min(100, width - 40);
  });

  readonly maxSize = computed(() => {
    const width = this.#width.value();
    if (!width) {
      return Number.MAX_SAFE_INTEGER;
    }

    return width - 40;
  });

  readonly leftSize = computed(() => {
    let val = this.#store.leftSize();
    if (!val) {
      const width = this.#width.value();
      if (width) {
        val = width * 0.2;
      }
      else {
        val = this.minSize();
      }
    }

    return Math.min(Math.max(val, this.minSize()), this.maxSize());
  });

  constructor() {
    toObservable(this.splitRef).pipe(
      mergeMap(split => split.dragProgress$),
      takeUntilDestroyed(),
    ).subscribe((evt) => {
      this.#store.updateLeftSize(evt.sizes[0] as number);
    });
  }

  onGutterDblClick() {
    const width = this.#width.value();
    if (width) {
      this.#store.updateLeftSize(width * 0.2);
    }
  }
}
