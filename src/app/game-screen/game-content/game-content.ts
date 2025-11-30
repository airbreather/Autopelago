import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  viewChild,
} from '@angular/core';

import { SplitAreaComponent, SplitComponent } from 'angular-split';
import type { AutopelagoClientAndData } from '../../data/slot-data';
import { GameStore } from '../../store/autopelago-store';
import { GameScreenStore } from '../../store/game-screen-store';
import { elementSizeSignal } from '../../utils/element-size';
import { GameTabs } from './game-tabs/game-tabs';
import { StatusDisplay } from './status-display/status-display';

@Component({
  selector: 'app-game-content',
  imports: [
    GameTabs,
    SplitAreaComponent,
    SplitComponent,
    StatusDisplay,
  ],
  providers: [
    GameScreenStore,
    GameStore,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #outer class="outer">
      <as-split unit="pixel" direction="horizontal" (dragEnd)="onSplitDragEnd($event.sizes[0])"
                gutterDblClickDuration="500" (gutterDblClick)="onGutterDblClick()">
        <as-split-area class="left" [size]="leftSize()" [minSize]="minSize()" [maxSize]="maxSize()">
          <app-status-display [game]="game()" />
        </as-split-area>
        <as-split-area class="right">
          <app-game-tabs [game]="game()" />
        </as-split-area>
      </as-split>
    </div>
  `,
  styles: `
    .outer {
      width: 100%;
      height: 100%;
    }
  `,
})
export class GameContent {
  readonly #store = inject(GameScreenStore, { self: true });
  readonly #gameStore = inject(GameStore, { self: true });
  readonly game = input.required<AutopelagoClientAndData>();
  protected readonly outerRef = viewChild.required<ElementRef<HTMLDivElement>>('outer');

  readonly #size = elementSizeSignal(this.outerRef);
  readonly #width = computed(() => this.#size().scrollWidth);

  protected readonly minSize = computed(() => {
    const width = this.#width();
    if (!width) {
      return 100;
    }

    return Math.min(100, width - 40);
  });

  protected readonly maxSize = computed(() => {
    const width = this.#width();
    if (!width) {
      return Number.MAX_SAFE_INTEGER;
    }

    return width - 40;
  });

  protected readonly leftSize = computed(() => {
    let val = this.#store.leftSize();
    if (!val) {
      const width = this.#width();
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
    this.#store.setScreenSizeSignal(this.#size);
    const initEffect = effect(() => {
      this.#gameStore.init(this.game());
      initEffect.destroy();
    });
    effect(() => {
      this.#sendUpdates();
    });
  }

  protected onSplitDragEnd(leftSize: number | '*') {
    if (leftSize !== '*') {
      this.#store.updateLeftSize(leftSize);
    }
  }

  protected onGutterDblClick() {
    const width = this.#width();
    if (width) {
      this.#store.updateLeftSize(width * 0.2);
    }
  }

  #prevSendUpdates: Promise<void> | null = null;

  #sendUpdates() {
    const { client, storedDataKey } = this.game();
    const newStoredData = this.#gameStore.asStoredData();
    const prevSendUpdates = this.#prevSendUpdates;
    if (!prevSendUpdates) {
      // the first time through, the value (by definition) hasn't changed from the initial state, so
      // there's no need to send this redundant update.
      this.#prevSendUpdates = Promise.resolve();
      return;
    }

    const mySendUpdates = this.#prevSendUpdates = prevSendUpdates.then(async () => {
      if (this.#prevSendUpdates !== mySendUpdates) {
        // another update was queued while we were waiting for this one to finish. this means that
        // there's a newer promise (a)waiting on us, so we can just return.
        return;
      }

      await client.storage
        .prepare(storedDataKey, newStoredData)
        .replace(newStoredData)
        .commit(true);
    });
  }
}
