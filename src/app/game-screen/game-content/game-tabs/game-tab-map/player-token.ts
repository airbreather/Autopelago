import { DestroyRef, effect, inject, resource, type Signal, untracked } from '@angular/core';
import { List } from 'immutable';
import { DropShadowFilter } from 'pixi-filters';
import { Assets, Sprite, Texture, Ticker } from 'pixi.js';
import Queue from 'yocto-queue';
import type { Vec2 } from '../../../../data/locations';
import type { AutopelagoDefinitions } from '../../../../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';

const ROTATION_SCALE = Math.PI / 3200;
const CYCLE = 1000;
const HALF_CYCLE = CYCLE / 2;
const QUARTER_CYCLE = CYCLE / 4;

export interface CreatePlayerTokenOptions {
  ticker: Ticker;
  averageTimeSeconds: Signal<number>;
  enableRatAnimations: Signal<boolean | null>;
  game: Signal<AutopelagoClientAndData | null>;
  defs: Signal<AutopelagoDefinitions>;
  currentLocation: Signal<number>;
  consumeOutgoingMoves(): List<readonly [number, number]>;
}

const SCALE = 0.25;
export function createPlayerToken(options: CreatePlayerTokenOptions) {
  const playerTokenContext = {
    playerToken: new Sprite(), // just to help the compiler
    cycleTime: 0,
    neutralAngle: 0,
    scaleX: SCALE,
  };
  const destroyRef = inject(DestroyRef);
  const playerTokenResource = resource({
    defaultValue: null,
    params: () => ({ enableRatAnimations: options.enableRatAnimations() }),
    loader: async ({ params }) => {
      if (params.enableRatAnimations === null) {
        return null;
      }

      const texture = await Assets.load<Texture>('assets/images/players/pack_rat.webp');
      const playerToken = playerTokenContext.playerToken = new Sprite(texture);
      playerToken.position.set(2, 80);
      playerToken.scale.set(SCALE);
      playerToken.filters = [new DropShadowFilter({
        blur: 1,
        offset: { x: 6, y: 6 },
        color: 'black',
      })];
      playerToken.anchor.set(0.5);

      if (params.enableRatAnimations) {
        function doRotation(this: typeof playerTokenContext, t: Ticker) {
          this.cycleTime = (this.cycleTime + t.deltaMS) % CYCLE;
          this.playerToken.scale.x = this.scaleX;
          this.playerToken.rotation = this.neutralAngle + (Math.abs(this.cycleTime - HALF_CYCLE) - QUARTER_CYCLE) * ROTATION_SCALE;
        }

        options.ticker.add(doRotation, playerTokenContext);
        destroyRef.onDestroy(() => options.ticker.remove(doRotation, playerTokenContext));
      }

      return playerToken;
    },
  });

  let everSetInitialPosition = false;
  let moveDuration = 100;

  let prog = 0;
  const queuedMoves = new Queue<{ from: Vec2; to: Vec2 }>();
  let animatePlayerMoveCallback: ((t: Ticker) => void) | null = null;
  effect(() => {
    const playerToken = playerTokenResource.value();
    const game = options.game();
    if (!playerToken || !game) {
      return;
    }

    const defs = options.defs();
    const moves = options.consumeOutgoingMoves();
    if (moves.size === 0) {
      if (!everSetInitialPosition) {
        const { allLocations } = defs;
        playerToken.position.set(...untracked(() => allLocations[options.currentLocation()].coords));
        everSetInitialPosition = true;
        moveDuration = 200 * Math.min(1, (game.connectScreenState.minTimeSeconds + game.connectScreenState.maxTimeSeconds) / 2);
      }

      return;
    }

    for (const [prev, next] of moves) {
      const prevCoords = defs.allLocations[prev].coords;
      const nextCoords = defs.allLocations[next].coords;
      queuedMoves.enqueue({ from: [...prevCoords], to: nextCoords });
    }

    if (animatePlayerMoveCallback !== null) {
      return;
    }

    animatePlayerMoveCallback = (t) => {
      if (animatePlayerMoveCallback === null) {
        return;
      }

      prog += t.deltaMS;
      if (queuedMoves.size > 6) {
        // there are too many moves. we really need to catch up. for every 6 moves over our magic
        // number of 6, increase the animation speed by a factor of 20%.
        t.speed = Math.pow(1.2, queuedMoves.size / 6);
      }
      else if (t.speed !== 1) {
        t.speed = 1;
      }

      while (prog >= moveDuration) {
        const fullMove = queuedMoves.dequeue();
        if (queuedMoves.size === 0 && fullMove !== undefined) {
          playerToken.position.set(...defs.allLocations[options.currentLocation()].coords);
          t.remove(animatePlayerMoveCallback);
          animatePlayerMoveCallback = null;
          prog = 0;
          t.speed = 1;
          return;
        }

        prog -= moveDuration;
      }

      const nextMove = queuedMoves.peek();
      if (nextMove === undefined) {
        playerToken.position.set(...defs.allLocations[options.currentLocation()].coords);
        t.remove(animatePlayerMoveCallback);
        animatePlayerMoveCallback = null;
        prog = 0;
        return;
      }

      const fraction = prog / moveDuration;
      const x = nextMove.from[0] + (nextMove.to[0] - nextMove.from[0]) * fraction;
      const y = nextMove.from[1] + (nextMove.to[1] - nextMove.from[1]) * fraction;
      playerToken.position.set(x, y);
      playerTokenContext.neutralAngle = Math.atan2(nextMove.to[1] - nextMove.from[1], nextMove.to[0] - nextMove.from[0]);
      if (Math.abs(playerTokenContext.neutralAngle) < Math.PI / 2) {
        playerTokenContext.scaleX = 0.25;
      }
      else {
        playerTokenContext.neutralAngle -= Math.PI;
        playerTokenContext.scaleX = -0.25;
      }
    };
    options.ticker.add(animatePlayerMoveCallback);
  });
  return playerTokenResource.asReadonly();
}
