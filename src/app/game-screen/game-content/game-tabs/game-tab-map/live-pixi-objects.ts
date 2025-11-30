import { Dialog } from '@angular/cdk/dialog';
import { computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { GraphicsContext, Sprite, type StrokeStyle, type Ticker } from 'pixi.js';
import Queue from 'yocto-queue';
import type { LandmarkYamlKey, Vec2 } from '../../../../data/locations';
import { type AutopelagoDefinitions } from '../../../../data/resolved-definitions';
import type { AnimatableAction } from '../../../../game/defining-state';
import { GameStore } from '../../../../store/autopelago-store';
import { GameScreenStore } from '../../../../store/game-screen-store';
import { arraysEqual, bitArraysEqual } from '../../../../utils/equal-helpers';
import { createFillerMarkers, type FillerMarkers } from './filler-markers';
import { createLandmarkMarkers, type LandmarkMarkers } from './landmark-markers';
import { createPlayerToken, SCALE, type WiggleOptimizationBox } from './player-token';
import { UWin } from './u-win';

interface ResolvedAction {
  run(fraction: number, defs: AutopelagoDefinitions, playerToken: Sprite, landmarkMarkers: LandmarkMarkers, fillerMarkers: FillerMarkers): void;
}

const DASH_LENGTH = 3;
const DASH_CYCLE_LENGTH = DASH_LENGTH * 2;
const STROKE_STYLE = {
  width: 1,
  color: 'red',
} as const satisfies StrokeStyle;
const SAMPLES_PER_PATH_LINE = 121; // this should be an odd number to avoid an edge case.
function buildPathLines(pts: readonly Vec2[]): readonly GraphicsContext[] {
  const result: GraphicsContext[] = [];
  for (let i = 0; i < SAMPLES_PER_PATH_LINE; i++) {
    const gfx = new GraphicsContext();
    result.push(gfx);
    gfx.setStrokeStyle(STROKE_STYLE);
    gfx.beginPath();
    const offsetPixels = (i / SAMPLES_PER_PATH_LINE) * DASH_CYCLE_LENGTH;
    let on = offsetPixels < DASH_LENGTH;
    if (on) {
      gfx.moveTo(...pts[0]);
    }
    for (const segment of dashedLineSegments(pts, offsetPixels)) {
      for (let j = 0; j < segment.length; j++) {
        if (on) {
          gfx.lineTo(...segment[j]);
        }
        else {
          gfx.moveTo(...segment[j]);
        }
        if (j < segment.length - 1) {
          on = !on;
        }
      }
    }
    gfx.stroke();
  }
  return result;
}

// the most popular library for dashed lines doesn't support offsets, and it doesn't have anything
// special about how it does the math, so just do it all inline with the constants weaved in. the
// usual dash style parameters we emulate are dashes of [4, 4] with an offset of 8 * offsetFraction
// (where 8 came from the sum of all lengths in the dash array).
function dashedLineSegments(pts: readonly Vec2[], offsetPixels: number): readonly (readonly Vec2[])[] {
  if (pts.length < 2) {
    return [pts];
  }

  const allStops: Vec2[][] = [];
  for (let i = 1; i < pts.length; i++) {
    const start = pts[i - 1];
    const end = pts[i];
    const stops: Vec2[] = [];
    allStops.push(stops);
    const len = Math.hypot(end[1] - start[1], end[0] - start[0]);
    const angle = Math.atan2(end[1] - start[1], end[0] - start[0]);
    const cos = Math.cos(angle);
    const sin = Math.sin(angle);
    // handle the remaining bit from the previous chunk. our caller can handle figuring out whether
    // the current segment is a dash or a gap based on starting from >=0.5 and alternating at every
    // stop that isn't at the end of an array.
    const halfOffsetPixels = offsetPixels > DASH_LENGTH ? offsetPixels - DASH_LENGTH : offsetPixels;
    if (len < DASH_LENGTH - halfOffsetPixels) {
      // whatever was happening at the start of this segment continues all the way through.
      stops.push(end);
      offsetPixels += len;
      continue;
    }

    let prevX = start[0] + halfOffsetPixels * cos;
    let prevY = start[1] + halfOffsetPixels * sin;
    stops.push([prevX, prevY]);
    const dx = DASH_LENGTH * cos;
    const dy = DASH_LENGTH * sin;
    let remainingInCurrentStop = len - halfOffsetPixels;
    while (remainingInCurrentStop > DASH_LENGTH) {
      stops.push([prevX += dx, prevY += dy]);
      remainingInCurrentStop -= DASH_LENGTH;
    }
    stops.push(end);
    if ((offsetPixels > DASH_LENGTH) !== (stops.length % 2 === 1)) {
      // flip
      offsetPixels = offsetPixels > DASH_LENGTH
        ? (DASH_LENGTH - remainingInCurrentStop)
        : (DASH_CYCLE_LENGTH - remainingInCurrentStop);
    }
    else {
      // keep the same
      offsetPixels = offsetPixels > DASH_LENGTH
        ? (DASH_CYCLE_LENGTH - remainingInCurrentStop)
        : (DASH_LENGTH - remainingInCurrentStop);
    }
  }
  return allStops;
}

const NO_ACTION = { run: () => { /* empty */ } } as const;
export function createLivePixiObjects(ticker: Ticker) {
  const store = inject(GameStore);
  const gameScreenStore = inject(GameScreenStore);
  const showingPath = gameScreenStore.showingPath;
  const mapSizeSignal = gameScreenStore.screenSizeSignal;
  const dialog = inject(Dialog);
  const destroyRef = inject(DestroyRef);
  const wiggleOptimizationBox: WiggleOptimizationBox = {
    neutralAngle: 0,
    scaleX: SCALE,
    _playerToken: new Sprite(), // will be overridden
    _cycleTime: 0, // will be overridden
  };
  function updateWiggleOptimizationBox(from: Vec2, to: Vec2) {
    wiggleOptimizationBox.neutralAngle = Math.atan2(to[1] - from[1], to[0] - from[0]);
    if (Math.abs(wiggleOptimizationBox.neutralAngle) < Math.PI / 2) {
      wiggleOptimizationBox.scaleX = SCALE;
    }
    else {
      wiggleOptimizationBox.neutralAngle -= Math.PI;
      wiggleOptimizationBox.scaleX = -SCALE;
    }
  }

  const playerTokenPosition = signal<Vec2>([0, 0]);
  const playerTokenResource = createPlayerToken({
    ticker,
    wiggleOptimizationBox,
    game: store.game,
    defs: store.defs,
    position: playerTokenPosition,
    consumeOutgoingAnimatableActions: store.consumeOutgoingAnimatableActions,
  });
  const landmarksResource = createLandmarkMarkers({
    ticker,
    game: store.game,
    defs: store.defs,
    victoryLocationYamlKey: store.victoryLocationYamlKey,
    regionIsLandmarkWithRequirementSatisfied: computed(() => store.regionLocks().regionIsLandmarkWithRequirementSatisfied, { equal: bitArraysEqual }),
  });
  const fillerMarkersSignal = createFillerMarkers({
    victoryLocationYamlKey: store.victoryLocationYamlKey,
  });

  function resolveAction(defs: AutopelagoDefinitions, action: AnimatableAction): ResolvedAction {
    if (action.type === 'move') {
      const fromCoords = defs.allLocations[action.fromLocation].coords;
      const toCoords = defs.allLocations[action.toLocation].coords;
      const dx = toCoords[0] - fromCoords[0];
      const dy = toCoords[1] - fromCoords[1];
      return {
        run: (fraction: number, _defs: unknown, playerToken: Sprite) => {
          const x = fromCoords[0] + dx * fraction;
          const y = fromCoords[1] + dy * fraction;
          playerToken.position.set(x, y);
          playerTokenPosition.set([x, y]);
          updateWiggleOptimizationBox(fromCoords, toCoords);
        },
      };
    }

    if (action.type === 'check-locations') {
      const fillerLocations: number[] = [];
      const landmarkLocations: LandmarkYamlKey[] = [];
      for (const location of action.locations) {
        if (Number.isNaN(defs.regionForLandmarkLocation[location])) {
          fillerLocations.push(location);
        }
        else {
          const region = defs.allRegions[defs.regionForLandmarkLocation[location]];
          // just satisfy the compiler. we know it'll be here.
          if (!('loc' in region)) {
            return NO_ACTION;
          }
          landmarkLocations.push(region.yamlKey);
        }
      }
      return {
        run: (fraction: number, _defs: unknown, _playerToken: unknown, landmarkMarkers: LandmarkMarkers, fillerMarkers: FillerMarkers) => {
          if (fraction !== 1) {
            return;
          }
          fillerMarkers.markChecked(...fillerLocations);
          const spriteLookup = landmarkMarkers.spriteLookup;
          for (const yamlKey of landmarkLocations) {
            if (yamlKey in spriteLookup) {
              const box = spriteLookup[yamlKey];
              if (box) {
                box.onSprite.visible = true;
                if ('offSprite' in box) {
                  box.offSprite.visible = false;
                  box.onQSprite.visible = false;
                  box.offQSprite.visible = false;
                }
              }
            }
          }
        },
      };
    }

    if (action.type === 'completed-goal') {
      return {
        run: (fraction: number, _defs: unknown, _playerToken: unknown, landmarkMarkers: LandmarkMarkers) => {
          if (fraction !== 1) {
            return;
          }

          const { spriteLookup } = landmarkMarkers;
          if ('moon_comma_the' in spriteLookup) {
            spriteLookup.moon_comma_the.onSprite.visible = true;
          }
        },
      };
    }

    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    if (action.type === 'u-win') {
      return {
        run: (fraction: number) => {
          if (fraction === 1) {
            dialog.open(UWin, { data: mapSizeSignal });
          }
        },
      };
    }

    return NO_ACTION;
  }

  let finishedFirstRound = false;
  let moveDuration = 100;

  let prog = 0;
  const queuedActions = new Queue<ResolvedAction>();
  let animatePlayerMoveCallback: ((t: Ticker) => void) | null = null;
  destroyRef.onDestroy(() => {
    if (animatePlayerMoveCallback !== null) {
      ticker.remove(animatePlayerMoveCallback);
      animatePlayerMoveCallback = null;
    }
  });

  let pathProg = 0;
  let animateShownPathCallback: ((t: Ticker) => void) | null = null;
  destroyRef.onDestroy(() => {
    if (animateShownPathCallback !== null) {
      ticker.remove(animateShownPathCallback);
      animateShownPathCallback = null;
    }
  });
  effect(() => {
    const playerToken = playerTokenResource.value();
    if (!playerToken) {
      return;
    }

    if (!showingPath()) {
      playerToken.pathGfx.visible = false;
      if (animateShownPathCallback !== null) {
        ticker.remove(animateShownPathCallback);
        animateShownPathCallback = null;
      }
      return;
    }

    if (animateShownPathCallback === null) {
      playerToken.pathGfx.visible = true;
    }
    else {
      ticker.remove(animateShownPathCallback);
    }

    const CYCLE_DURATION = 4000;
    const { allLocations } = store.defs();
    const coords = store.targetLocationRoute().map(l => allLocations[l].coords);
    const lines = buildPathLines(coords);
    const scale = lines.length / CYCLE_DURATION;
    animateShownPathCallback = (t) => {
      pathProg = (pathProg + t.deltaMS) % CYCLE_DURATION;
      playerToken.pathGfx.context = lines[Math.floor(pathProg * scale)];
    };
    ticker.add(animateShownPathCallback);
  });
  effect(() => {
    const game = store.game();
    const playerToken = playerTokenResource.value();
    const landmarkMarkers = landmarksResource.value();
    if (!(game && playerToken && landmarkMarkers)) {
      return;
    }

    const playerTokenSprite = playerToken.sprite;
    const fillerMarkers = fillerMarkersSignal();
    const defs = store.defs();
    const actions = store.consumeOutgoingAnimatableActions();
    if (actions.size === 0) {
      if (!finishedFirstRound) {
        untracked(() => {
          const currentCoords = defs.allLocations[store.currentLocation()].coords;
          const targetCoords = defs.allLocations[store.targetLocation()].coords;
          playerTokenSprite.position.set(...currentCoords);
          playerTokenPosition.set(currentCoords);
          let fromCoords = currentCoords;
          if (arraysEqual(currentCoords, targetCoords)) {
            const possiblePrev = defs.allLocations[store.currentLocation()].connected.backward.at(0);
            if (possiblePrev !== undefined) {
              fromCoords = defs.allLocations[possiblePrev].coords;
            }
          }
          updateWiggleOptimizationBox(fromCoords, targetCoords);
          const fillersToMark: number[] = [];
          for (const location of store.checkedLocations()) {
            if (Number.isNaN(defs.regionForLandmarkLocation[location])) {
              fillersToMark.push(location);
            }
            else {
              const region = defs.allRegions[defs.regionForLandmarkLocation[location]];
              // just satisfy the compiler. we know it'll be here.
              if ('loc' in region) {
                const spriteBox = landmarkMarkers.spriteLookup[region.yamlKey];
                if (spriteBox) {
                  spriteBox.onSprite.visible = true;
                  if ('offSprite' in spriteBox) {
                    spriteBox.offSprite.visible = false;
                    spriteBox.onQSprite.visible = false;
                    spriteBox.offQSprite.visible = false;
                  }
                }
              }
            }
          }

          fillerMarkers.markChecked(...fillersToMark);
        });

        finishedFirstRound = true;
        moveDuration = 200 * Math.min(1, (game.connectScreenState.minTimeSeconds + game.connectScreenState.maxTimeSeconds) / 2);
      }

      return;
    }

    for (const action of actions) {
      queuedActions.enqueue(resolveAction(defs, action));
    }

    if (animatePlayerMoveCallback !== null) {
      return;
    }

    animatePlayerMoveCallback = (t) => {
      if (animatePlayerMoveCallback === null) {
        return;
      }

      prog += t.deltaMS;
      if (queuedActions.size > 6) {
        // there are too many moves. we really need to catch up. for every 6 moves over our magic
        // number of 6, increase the animation speed by a factor of 20%. up to 5x, though.
        t.speed = Math.min(5, Math.pow(1.2, queuedActions.size / 6));
      }
      else if (t.speed !== 1) {
        t.speed = 1;
      }

      while (prog >= moveDuration) {
        const fullMove = queuedActions.dequeue();
        if (fullMove === undefined) {
          break;
        }

        fullMove.run(1, defs, playerToken.sprite, landmarkMarkers, fillerMarkers);
        prog -= moveDuration;
      }

      const nextMove = queuedActions.peek();
      if (nextMove === undefined) {
        const fromCoords = defs.allLocations[store.currentLocation()].coords;
        const toCoords = defs.allLocations[store.targetLocation()].coords;
        playerToken.sprite.position.set(...fromCoords);
        playerTokenPosition.set(fromCoords);
        if (store.currentLocation() !== store.targetLocation()) {
          updateWiggleOptimizationBox(fromCoords, toCoords);
        }
        t.remove(animatePlayerMoveCallback);
        animatePlayerMoveCallback = null;
        prog = 0;
        t.speed = 1;
        return;
      }

      nextMove.run(prog / moveDuration, defs, playerToken.sprite, landmarkMarkers, fillerMarkers);
    };
    ticker.add(animatePlayerMoveCallback);
  });

  return {
    playerTokenResource,
    landmarksResource,
    fillerMarkersSignal,
  };
}
