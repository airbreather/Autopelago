import {
  AfterViewInit,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  OnDestroy,
  viewChild,
} from '@angular/core';
import { rxResource, takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';

import { map, mergeMap } from 'rxjs';

import { SplitAreaComponent, SplitComponent } from 'angular-split';

import { ArchipelagoClient } from '../archipelago-client';
import { GameScreenStore } from '../store/game-screen-store';
import { GameTabs } from './game-tabs/game-tabs';
import { PixiService } from './pixi-service';
import { StatusDisplay } from './status-display/status-display';
import { resizeEvents } from '../util';

@Component({
  selector: 'app-game-screen',
  imports: [
    SplitComponent, SplitAreaComponent, StatusDisplay, GameTabs,
  ],
  providers: [PixiService],
  template: `
    <div #outer class="outer">
      <as-split #split unit="pixel" direction="horizontal"
                gutterDblClickDuration="500" (gutterDblClick)="onGutterDblClick()">
        <as-split-area class="left" [size]="leftSize()" [minSize]="minSize()" [maxSize]="maxSize()">
          <app-status-display />
        </as-split-area>
        <as-split-area class="right">
          <app-game-tabs />
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
export class GameScreen implements AfterViewInit, OnDestroy {
  readonly #store = inject(GameScreenStore);
  readonly #archipelagoClient = inject(ArchipelagoClient);
  readonly #pixiService = inject(PixiService, { self: true });
  readonly #destroyRef = inject(DestroyRef);
  protected readonly splitRef = viewChild.required<SplitComponent>('split');
  protected readonly outerRef = viewChild.required<ElementRef<HTMLDivElement>>('outer');

  readonly #width = rxResource<number | null, ElementRef<HTMLDivElement>>({
    defaultValue: null,
    params: () => this.outerRef(),
    stream: ({ params: outerRef }) => {
      return resizeEvents(outerRef.nativeElement).pipe(
        map(() => outerRef.nativeElement.scrollWidth),
        takeUntilDestroyed(this.#destroyRef),
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

  ngAfterViewInit() {
    void this.#pixiService.init();
  }

  ngOnDestroy() {
    this.#archipelagoClient.disconnect();
  }

  onGutterDblClick() {
    const width = this.#width.value();
    if (width) {
      this.#store.updateLeftSize(width * 0.2);
    }
  }
}
