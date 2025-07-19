import {
  AfterViewInit,
  Component,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  Signal,
  viewChild,
} from '@angular/core';

import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';

import { Subscription } from 'rxjs';

import { ConnectScreenStore } from '../../../store/connect-screen.store';
import { resizeEvents } from '../../../util';

function fitTextToContainer(inner: HTMLElement, outer: HTMLElement, max: number) {
  let fontSize = window.getComputedStyle(inner).fontSize;

  while (inner.scrollWidth <= outer.clientWidth) {
    const fontSizeNum = Math.min(max, Number(/^\d+/.exec(fontSize)) + 5);
    if (fontSizeNum >= max) {
      break;
    }

    fontSize = fontSize.replace(/^\d+/, fontSizeNum.toString());
    inner.style.fontSize = fontSize;
  }

  while (inner.scrollWidth > outer.clientWidth) {
    fontSize = fontSize.replace(/^\d+/, s => (Number(s) - 1).toString());
    inner.style.fontSize = fontSize;
  }
}

@Component({
  selector: 'app-player-name-and-navigation',
  imports: [
    RouterLink,
  ],
  template: `
    <div #outer class="outer">
      <div #playerNameDiv class="player-name">{{ playerName() }}</div>
      <div><button #returnButton class="return-button" routerLink="/">Back to Main Menu</button></div>
    </div>
  `,
  styles: `
    .outer {
      text-align: left;
      margin-left: 5px;
      margin-right: 5px;
    }

    .player-name {
      font-size: 50px;
    }

    .return-button {
      font-size: 30px;
      text-wrap: nowrap;
    }
  `,
})
export class PlayerNameAndNavigation implements AfterViewInit {
  readonly #connectScreenStore = inject(ConnectScreenStore);
  readonly #destroy = inject(DestroyRef);
  readonly #injector = inject(Injector);

  readonly playerName = this.#connectScreenStore.slot;

  readonly outerElement = viewChild.required<ElementRef<HTMLElement>>('outer');
  readonly playerNameElement = viewChild.required<ElementRef<HTMLElement>>('playerNameDiv');
  readonly returnButtonElement = viewChild.required<ElementRef<HTMLElement>>('returnButton');

  ngAfterViewInit(): void {
    this.#resizeText(this.playerNameElement, 50);
    this.#resizeText(this.returnButtonElement, 30);
  }

  #resizeText(innerRef: Signal<ElementRef<HTMLElement>>, max: number) {
    let prevSub = new Subscription();
    effect(() => {
      prevSub.unsubscribe();
      const outer = this.outerElement().nativeElement;
      const inner = innerRef().nativeElement;
      prevSub = resizeEvents(outer)
        .pipe(takeUntilDestroyed(this.#destroy))
        .subscribe(() => {
          fitTextToContainer(inner, outer, max);
        });
    }, { injector: this.#injector });
  }
}
