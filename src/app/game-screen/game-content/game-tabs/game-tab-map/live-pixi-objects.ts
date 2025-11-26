import { effect, untracked } from '@angular/core';
import { Sprite, type Ticker } from 'pixi.js';
import Queue from 'yocto-queue';
import type { Vec2 } from '../../../../data/locations';
import { GameStore } from '../../../../store/autopelago-store';
import { createFillerMarkers } from './filler-markers';
import { createLandmarkMarkers } from './landmark-markers';
import { createPlayerToken, SCALE, type WiggleOptimizationBox } from './player-token';

export function createLivePixiObjects(store: InstanceType<typeof GameStore>, ticker: Ticker) {
  const wiggleOptimizationBox: WiggleOptimizationBox = {
    neutralAngle: 0,
    scaleX: SCALE,
    _playerToken: new Sprite(), // will be overridden
    _cycleTime: 0, // will be overridden
  };
  const playerTokenResource = createPlayerToken({
    ticker,
    wiggleOptimizationBox,
    game: store.game,
    defs: store.defs,
    currentLocation: store.currentLocation,
    consumeOutgoingMoves: store.consumeOutgoingMoves,
  });
  const landmarksResource = createLandmarkMarkers({
    ticker,
    game: store.game,
    defs: store.defs,
    victoryLocationYamlKey: store.victoryLocationYamlKey,
    regionIsLandmarkWithUnsatisfiedRequirement: store.regionIsLandmarkWithUnsatisfiedRequirement,
    checkedLocations: store.checkedLocations,
  });
  const fillerMarkersSignal = createFillerMarkers({
    victoryLocationYamlKey: store.victoryLocationYamlKey,
    checkedLocations: store.checkedLocations,
  });

  let everSetInitialPosition = false;
  let moveDuration = 100;

  let prog = 0;
  const queuedMoves = new Queue<{ from: Vec2; to: Vec2 }>();
  let animatePlayerMoveCallback: ((t: Ticker) => void) | null = null;
  effect(() => {
    const playerToken = playerTokenResource.value();
    const game = store.game();
    if (!playerToken || !game) {
      return;
    }

    const defs = store.defs();
    const moves = store.consumeOutgoingMoves();
    if (moves.size === 0) {
      if (!everSetInitialPosition) {
        const { allLocations } = defs;
        playerToken.position.set(...untracked(() => allLocations[store.currentLocation()].coords));
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
          playerToken.position.set(...defs.allLocations[store.currentLocation()].coords);
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
        playerToken.position.set(...defs.allLocations[store.currentLocation()].coords);
        t.remove(animatePlayerMoveCallback);
        animatePlayerMoveCallback = null;
        prog = 0;
        return;
      }

      const fraction = prog / moveDuration;
      const x = nextMove.from[0] + (nextMove.to[0] - nextMove.from[0]) * fraction;
      const y = nextMove.from[1] + (nextMove.to[1] - nextMove.from[1]) * fraction;
      playerToken.position.set(x, y);
      wiggleOptimizationBox.neutralAngle = Math.atan2(nextMove.to[1] - nextMove.from[1], nextMove.to[0] - nextMove.from[0]);
      if (Math.abs(wiggleOptimizationBox.neutralAngle) < Math.PI / 2) {
        wiggleOptimizationBox.scaleX = 0.25;
      }
      else {
        wiggleOptimizationBox.neutralAngle -= Math.PI;
        wiggleOptimizationBox.scaleX = -0.25;
      }
    };
    ticker.add(animatePlayerMoveCallback);
  });

  return {
    playerTokenResource,
    landmarksResource,
    fillerMarkersSignal,
  };
}
