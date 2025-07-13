import { Component, inject, viewChild } from '@angular/core';
import { SplitAreaComponent, SplitComponent } from "angular-split";
import { takeUntilDestroyed, toObservable } from "@angular/core/rxjs-interop";
import { GameScreenStore } from "../store/game-screen-store";
import { mergeMap } from "rxjs/operators";

@Component({
  selector: 'app-game-screen',
  imports: [
    SplitComponent, SplitAreaComponent,
  ],
  template: `
    <as-split class="outer" unit="percent" direction="horizontal" gutterDblClickDuration="500" (gutterDblClick)="onGutterDblClick()" #x>
      <as-split-area [size]="leftSize()" [minSize]="5" [maxSize]="95">
        <p>!</p>
      </as-split-area>
      <as-split-area>
        <p>?</p>
      </as-split-area>
    </as-split>
  `,
  styles: `
    .outer {
      width: 100vw;
      height: 100vh;
    }
  `
})
export class GameScreen {
  readonly #store = inject(GameScreenStore);
  protected readonly splitRef = viewChild.required<SplitComponent>('x');

  leftSize = this.#store.leftSize;

  constructor() {
    toObservable(this.splitRef).pipe(
      mergeMap(split => split.dragProgress$),
      takeUntilDestroyed(),
    ).subscribe(evt => {
      this.#store.updateLeftSize(evt.sizes[0] as number);
    });
  }

  onGutterDblClick() {
    this.#store.restoreDefaultLeftSize();
  }
}
