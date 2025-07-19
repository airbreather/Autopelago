import { Component, inject, OnDestroy, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';

import { mergeMap } from 'rxjs';

import { SplitAreaComponent, SplitComponent } from 'angular-split';

import { ArchipelagoClient } from '../archipelago-client';
import { GameScreenStore } from '../store/game-screen-store';
import { GameTabs } from './game-tabs/game-tabs';
import { StatusDisplay } from './status-display/status-display';

@Component({
  selector: 'app-game-screen',
  imports: [
    SplitComponent, SplitAreaComponent, StatusDisplay, GameTabs,
  ],
  template: `
    <as-split #outer class="outer" unit="percent" direction="horizontal"
              gutterDblClickDuration="500" (gutterDblClick)="onGutterDblClick()">
      <as-split-area class="left" [size]="leftSize()" [minSize]="5" [maxSize]="95">
        <app-status-display />
      </as-split-area>
      <as-split-area class="right">
        <app-game-tabs />
      </as-split-area>
    </as-split>
  `,
  styles: `
    .outer {
      width: 100vw;
      height: 100vh;
    }
  `,
})
export class GameScreen implements OnDestroy {
  readonly #store = inject(GameScreenStore);
  readonly #archipelagoClient = inject(ArchipelagoClient);
  protected readonly splitRef = viewChild.required<SplitComponent>('outer');

  leftSize = this.#store.leftSize;

  constructor() {
    toObservable(this.splitRef).pipe(
      mergeMap(split => split.dragProgress$),
      takeUntilDestroyed(),
    ).subscribe((evt) => {
      this.#store.updateLeftSize(evt.sizes[0] as number);
    });
  }

  ngOnDestroy() {
    this.#archipelagoClient.disconnect();
  }

  onGutterDblClick() {
    this.#store.restoreDefaultLeftSize();
  }
}
