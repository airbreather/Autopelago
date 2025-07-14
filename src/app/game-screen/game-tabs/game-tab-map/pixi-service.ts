import { fromEvent } from "rxjs";

import { Application, Container } from "pixi.js";

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

  #beforeInit: RatPixiPlugin['beforeInit'];
  #afterInit: RatPixiPlugin['afterInit'];

  registerPlugin(plugin: RatPixiPlugin) {
    this.#beforeInit = chain(this.#beforeInit, plugin.beforeInit);
    this.#afterInit = chain(this.#afterInit, plugin.afterInit);
  }

  async init(canvas: HTMLCanvasElement, outer: HTMLDivElement) {
    const reciprocalOriginalWidth = 1 / 300.0;
    const reciprocalOriginalHeight = 1 / 450.0;
    const root = this.#app.stage;
    await this.#beforeInit?.(this.#app, root);
    await this.#app.init({ canvas, resizeTo: outer, backgroundAlpha: 0, antialias: false });
    root.scale.x = canvas.width * reciprocalOriginalWidth;
    root.scale.y = canvas.height * reciprocalOriginalHeight;
    fromEvent(canvas, 'resize').subscribe(() => {
      root.scale.x = canvas.width * reciprocalOriginalWidth;
      root.scale.y = canvas.height * reciprocalOriginalHeight;
    });
    await this.#afterInit?.(this.#app, root);
  }
}
