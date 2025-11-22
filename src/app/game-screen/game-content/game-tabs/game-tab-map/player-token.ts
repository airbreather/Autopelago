import { DestroyRef, effect, inject, resource, type Signal, untracked } from '@angular/core';
import { DropShadowFilter } from 'pixi-filters';
import { Assets, Sprite, Texture, Ticker } from 'pixi.js';
import Queue from 'yocto-queue';
import type { Vec2 } from '../../../../data/locations';
import type { GameStore } from '../../../../store/autopelago-store';

const ROTATION_SCALE = Math.PI / 3200;
const CYCLE = 1000;
const HALF_CYCLE = CYCLE / 2;
const QUARTER_CYCLE = CYCLE / 4;

export interface CreatePlayerTokenOptions {
  store: InstanceType<typeof GameStore>;
  ticker: Ticker;
  enableRatAnimationsSignal: Signal<boolean | null>;
}

const SCALE = 0.25;
export function createPlayerToken({ store, ticker, enableRatAnimationsSignal }: CreatePlayerTokenOptions) {
  const playerTokenContext = {
    playerToken: new Sprite(), // just to help the compiler
    cycleTime: 0,
    neutralAngle: 0,
    scaleX: SCALE,
  };
  const destroyRef = inject(DestroyRef);
  const playerTokenResource = resource({
    defaultValue: null,
    params: () => enableRatAnimationsSignal(),
    loader: async ({ params: enableRatAnimations }) => {
      if (enableRatAnimations === null) {
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

      if (enableRatAnimations) {
        function doRotation(this: typeof playerTokenContext, t: Ticker) {
          this.cycleTime = (this.cycleTime + t.deltaMS) % CYCLE;
          this.playerToken.scale.x = this.scaleX;
          this.playerToken.rotation = this.neutralAngle + (Math.abs(this.cycleTime - HALF_CYCLE) - QUARTER_CYCLE) * ROTATION_SCALE;
        }

        ticker.add(doRotation, playerTokenContext);
        destroyRef.onDestroy(() => ticker.remove(doRotation, playerTokenContext));
      }

      return playerToken;
    },
  });

  let everSetInitialPosition = false;

  const MOVE_DUR = 200;
  let prog = 0;
  const queuedMoves = new Queue<{ from: Vec2; to: Vec2 }>();
  let animatePlayerMoveCallback: ((t: Ticker) => void) | null = null;
  effect(() => {
    const playerToken = playerTokenResource.value();
    if (!playerToken) {
      return;
    }

    const defs = store.defs();
    const moves = store.consumeOutgoingMoves();
    if (moves.size === 0) {
      if (!everSetInitialPosition) {
        const { allLocations } = defs;
        playerToken.position.set(...untracked(() => allLocations[store.currentLocation()].coords));
        everSetInitialPosition = true;
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
      while (prog >= MOVE_DUR) {
        const fullMove = queuedMoves.dequeue();
        if (queuedMoves.size === 0 && fullMove !== undefined) {
          playerToken.position.set(...defs.allLocations[store.currentLocation()].coords);
          t.remove(animatePlayerMoveCallback);
          animatePlayerMoveCallback = null;
          prog = 0;
          return;
        }

        prog -= MOVE_DUR;
      }

      const nextMove = queuedMoves.peek();
      if (nextMove === undefined) {
        playerToken.position.set(...defs.allLocations[store.currentLocation()].coords);
        t.remove(animatePlayerMoveCallback);
        animatePlayerMoveCallback = null;
        prog = 0;
        return;
      }

      const fraction = prog / MOVE_DUR;
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
    ticker.add(animatePlayerMoveCallback);
  });
  return playerTokenResource.asReadonly();
}
