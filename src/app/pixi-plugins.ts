import { DestroyRef, effect, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Application, Container, Ticker } from 'pixi.js';

import { GameStoreService } from './store/autopelago-store';
import { resizeEvents } from './util';

export interface RatPixiPlugin {
  destroyRef?: DestroyRef;
  afterInit?(this: void, app: Application, root: Container): PromiseLike<void> | void;
}

@Injectable()
export class PixiPlugins {
  #pixiApplication: Application | null = null;
  readonly #plugins: RatPixiPlugin[] = [];
  readonly #destroyRef = inject(DestroyRef);
  readonly #gameStore = inject(GameStoreService);

  constructor() {
    effect(() => {
      if (this.#gameStore.paused()) {
        Ticker.shared.stop();
      }
      else {
        Ticker.shared.start();
      }
    });
  }

  registerPlugin(plugin: Readonly<RatPixiPlugin>) {
    this.#plugins.push(plugin);
    if (plugin.destroyRef) {
      plugin.destroyRef.onDestroy(() => {
        this.#plugins.splice(this.#plugins.indexOf(plugin), 1);
      });
    }
  }

  async initInterface(canvas: HTMLCanvasElement, outer: HTMLDivElement) {
    if (this.#pixiApplication) {
      throw new Error('Already initialized');
    }

    Ticker.shared.stop();
    const app = this.#pixiApplication = new Application();
    this.#destroyRef.onDestroy(() => {
      app.destroy();
      this.#pixiApplication = null;
    });
    const reciprocalOriginalWidth = 1 / canvas.width;
    const reciprocalOriginalHeight = 1 / canvas.height;
    await app.init({ canvas, resizeTo: outer, backgroundAlpha: 0, antialias: false, sharedTicker: true, autoStart: false });
    Ticker.shared.stop();

    resizeEvents(outer).pipe(
      // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
      takeUntilDestroyed(this.#destroyRef),
    ).subscribe(({ target }) => {
      app.stage.scale.x = target.clientWidth * reciprocalOriginalWidth;
      app.stage.scale.y = target.clientHeight * reciprocalOriginalHeight;
      app.resize();
    });

    for (const plugin of this.#plugins) {
      if (plugin.afterInit) {
        await plugin.afterInit(app, app.stage);
        Ticker.shared.stop();
      }
    }

    if (this.#gameStore.paused()) {
      app.resize();
      app.render();
    }
    else {
      Ticker.shared.start();
    }
  }
}
