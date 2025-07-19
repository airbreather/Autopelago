import { Application, Container } from 'pixi.js';
import { resizeEvents } from '../../../util';
import { DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

export interface RatPixiPlugin {
  beforeInit?(this: void, app: Application, root: Container): PromiseLike<void> | void;
  afterInit?(this: void, app: Application, root: Container): PromiseLike<void> | void;
}

function chain<Args extends unknown[]>(
  first?: (this: void, ...args: Args) => PromiseLike<void> | void,
  second?: (this: void, ...args: Args) => PromiseLike<void> | void,
): ((this: void, ...args: Args) => PromiseLike<void> | void) | undefined {
  if (!first) {
    return second;
  }

  if (!second) {
    return first;
  }

  return (...args) => {
    const res = first(...args);
    return res
      ? res.then(() => second(...args))
      : second(...args);
  };
}

export class PixiService {
  readonly #app = new Application();
  readonly #destroyRef = inject(DestroyRef);

  #beforeInit: RatPixiPlugin['beforeInit'];
  #afterInit: RatPixiPlugin['afterInit'];

  constructor() {
    this.#destroyRef.onDestroy(() => {
      this.#app.destroy();
    });
  }

  registerPlugin(plugin: RatPixiPlugin) {
    this.#beforeInit = chain(this.#beforeInit, plugin.beforeInit);
    this.#afterInit = chain(this.#afterInit, plugin.afterInit);
  }

  async init(canvas: HTMLCanvasElement, outer: HTMLDivElement) {
    const reciprocalOriginalWidth = 1 / 300.0;
    const reciprocalOriginalHeight = 1 / 450.0;
    const root = this.#app.stage;
    await this.#beforeInit?.(this.#app, root);
    await this.#app.init({ canvas, resizeTo: outer, backgroundAlpha: 0, antialias: false, autoStart: false });
    resizeEvents(canvas).pipe(
      // no need for a startWith: https://stackoverflow.com/a/60026394/1083771
      takeUntilDestroyed(this.#destroyRef),
    ).subscribe(({ target }) => {
      root.scale.x = target.width * reciprocalOriginalWidth;
      root.scale.y = target.height * reciprocalOriginalHeight;
    });
    await this.#afterInit?.(this.#app, root);
    this.#app.start();
  }
}
