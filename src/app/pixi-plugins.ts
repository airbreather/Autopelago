import { DestroyRef, effect, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Application, Ticker } from 'pixi.js';

import { GameStore } from './store/autopelago-store';
import { resizeEvents } from './util';

export interface RatPixiPlugin {
  destroyRef?: DestroyRef;
  afterInit?(this: void, app: Application): void;
}

@Injectable()
export class PixiPlugins {
  #pixiApplication: Application | null = null;
  #initialized = false;
  readonly #plugins: RatPixiPlugin[] = [];
  readonly #destroyRef = inject(DestroyRef);
  readonly #gameStore = inject(GameStore);

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

    if (this.#pixiApplication && this.#initialized && plugin.afterInit) {
      plugin.afterInit(this.#pixiApplication);
      if (this.#gameStore.paused()) {
        this.#pixiApplication.render();
        Ticker.shared.stop();
      }
    }
  }

  async initInterface(canvas: HTMLCanvasElement, outer: HTMLDivElement, interfaceDestroyRef: DestroyRef) {
    if (this.#pixiApplication) {
      if (this.#initialized) {
        throw new Error('Already initialized');
      }

      this.#destroyApp();
    }

    Ticker.shared.stop();
    const app = this.#pixiApplication = new Application();
    this.#destroyRef.onDestroy(() => {
      this.#destroyApp();
    });
    interfaceDestroyRef.onDestroy(() => {
      this.#destroyApp();
    });
    const reciprocalOriginalWidth = 1 / canvas.width;
    const reciprocalOriginalHeight = 1 / canvas.height;
    await app.init({ canvas, resizeTo: outer, backgroundAlpha: 0, antialias: false, sharedTicker: true, autoStart: false });
    this.#initialized = true;
    Ticker.shared.stop();

    resizeEvents(outer).pipe(
      // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
      takeUntilDestroyed(this.#destroyRef),
      takeUntilDestroyed(interfaceDestroyRef),
    ).subscribe(({ target }) => {
      app.stage.scale.x = target.clientWidth * reciprocalOriginalWidth;
      app.stage.scale.y = target.clientHeight * reciprocalOriginalHeight;
      app.resize();
    });

    for (const plugin of this.#plugins) {
      if (plugin.afterInit) {
        plugin.afterInit(app);
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

  #destroyApp() {
    if (this.#pixiApplication) {
      this.#pixiApplication.destroy();
      this.#pixiApplication = null;
      this.#initialized = false;
    }
  }
}
