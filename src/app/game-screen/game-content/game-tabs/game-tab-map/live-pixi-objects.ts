import { Dialog } from '@angular/cdk/dialog';
import { computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { GraphicsContext, Sprite, type StrokeStyle, type Ticker } from 'pixi.js';
import Queue from 'yocto-queue';
import type { LandmarkYamlKey, Vec2 } from '../../../../data/locations';
import type { AnimatableAction } from '../../../../game/defining-state';
import { GameStore } from '../../../../store/autopelago-store';
import { GameScreenStore } from '../../../../store/game-screen-store';
import { arraysEqual, bitArraysEqual } from '../../../../utils/equal-helpers';
import { createFillerMarkers, type FillerMarkers } from './filler-markers';
import { createLandmarkMarkers, type LandmarkMarkers } from './landmark-markers';
import { createPlayerToken, type PlayerTokenResult, SCALE, type WiggleOptimizationBox } from './player-token';
import { UWin } from './u-win';

interface ResolvedAction {
  run(fraction: number, playerToken: PlayerTokenResult, landmarkMarkers: LandmarkMarkers, fillerMarkers: FillerMarkers): void;
}

const DASH_LENGTH = 4;
const DASH_CYCLE_LENGTH = DASH_LENGTH * 2;
const STROKE_STYLE = {
  width: 1,
  color: 'red',
  join: 'round',
} as const satisfies StrokeStyle;
const SAMPLES_PER_PATH_LINE = 60;
function buildPathLines(pts: readonly Vec2[]): readonly GraphicsContext[] {
  const result: GraphicsContext[] = [];
  for (let i = 0; i < SAMPLES_PER_PATH_LINE; i++) {
    result.push(buildPathLine(pts, (i / SAMPLES_PER_PATH_LINE) * DASH_CYCLE_LENGTH));
  }
  return result;
}

interface PathStop {
  readonly target: Vec2;
  readonly stroke: boolean;
}
function buildPathLine(pts: readonly Vec2[], offsetPixels: number) {
  const gfx = new GraphicsContext();
  gfx.setStrokeStyle(STROKE_STYLE);
  gfx.beginPath();
  let prevMove: Vec2 | null = null;
  for (const { target, stroke } of dashedLineSegments(pts, offsetPixels)) {
    if (stroke) {
      if (prevMove !== null) {
        gfx.moveTo(...prevMove);
        prevMove = null;
      }
      gfx.lineTo(...target);
    }
    else {
      prevMove = target;
    }
  }
  gfx.stroke();
  return gfx;
}

// the most popular library for dashed lines doesn't support offsets, and it doesn't have anything
// special about how it does the math, so just do it all inline with the constants weaved in. the
// usual dash style parameters we emulate are dashes of [4, 4] with an offset of 8 * offsetFraction
// (where 8 came from the sum of all lengths in the dash array).
function dashedLineSegments(pts: readonly Vec2[], offsetPixels: number): readonly PathStop[] {
  if (pts.length < 2) {
    return [{ target: pts[0], stroke: false }];
  }

  let stroke = offsetPixels < DASH_LENGTH;
  let remainingInCurrentDashOrGap = (stroke ? DASH_LENGTH : DASH_CYCLE_LENGTH) - offsetPixels;
  const result: PathStop[] = [{ target: pts[0], stroke: false }];
  for (let i = 1; i < pts.length; i++) {
    const start = pts[i - 1];
    const end = pts[i];
    const len = Math.hypot(end[1] - start[1], end[0] - start[0]);
    const angle = Math.atan2(end[1] - start[1], end[0] - start[0]);
    const cos = Math.cos(angle);
    const sin = Math.sin(angle);
    if (len < remainingInCurrentDashOrGap) {
      // whatever was happening at the start of this segment continues all the way through.
      result.push({ target: end, stroke });
      remainingInCurrentDashOrGap -= len;
      continue;
    }

    let prevX = start[0] + remainingInCurrentDashOrGap * cos;
    let prevY = start[1] + remainingInCurrentDashOrGap * sin;
    result.push({ target: [prevX, prevY], stroke });
    stroke = !stroke;
    const dx = DASH_LENGTH * cos;
    const dy = DASH_LENGTH * sin;
    let remainingInCurrentStop = len - remainingInCurrentDashOrGap;
    while (remainingInCurrentStop > DASH_LENGTH) {
      result.push({ target: [prevX += dx, prevY += dy], stroke });
      remainingInCurrentStop -= DASH_LENGTH;
      stroke = !stroke;
    }
    result.push({ target: end, stroke });
    remainingInCurrentDashOrGap = DASH_LENGTH - remainingInCurrentStop;
  }
  return result;
}

const NO_ACTION = { run: () => { /* empty */ } } as const;
export function createLivePixiObjects(ticker: Ticker) {
  const store = inject(GameStore);
  const gameScreenStore = inject(GameScreenStore);
  const showingPath = gameScreenStore.showingPath;
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

  function resolveAction(action: AnimatableAction): ResolvedAction {
    if (action.type === 'move') {
      const defs = store.defs();
      const fromCoords = defs.allLocations[action.fromLocation].coords;
      const toCoords = defs.allLocations[action.toLocation].coords;
      const dx = toCoords[0] - fromCoords[0];
      const dy = toCoords[1] - fromCoords[1];
      return {
        run: (fraction: number, playerToken: PlayerTokenResult) => {
          const x = fromCoords[0] + dx * fraction;
          const y = fromCoords[1] + dy * fraction;
          playerToken.sprite.position.set(x, y);
          playerTokenPosition.set([x, y]);
          updateWiggleOptimizationBox(fromCoords, toCoords);
        },
      };
    }

    if (action.type === 'check-locations') {
      const defs = store.defs();
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
        run: (fraction: number, _playerToken: unknown, landmarkMarkers: LandmarkMarkers, fillerMarkers: FillerMarkers) => {
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
        run: (fraction: number, _playerToken: unknown, landmarkMarkers: LandmarkMarkers) => {
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
            dialog.open(UWin, {
              width: '60%',
              height: '60%',
            });
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
      playerToken.targetX.visible = false;
      if (animateShownPathCallback !== null) {
        ticker.remove(animateShownPathCallback);
        animateShownPathCallback = null;
      }
      return;
    }

    if (animateShownPathCallback === null) {
      playerToken.pathGfx.visible = true;
      playerToken.targetX.visible = true;
    }
    else {
      ticker.remove(animateShownPathCallback);
    }

    const CYCLE_DURATION = 1000;
    const { allLocations } = store.defs();
    const targetLocationRoute = store.targetLocationRoute();
    const coords = targetLocationRoute.map(l => allLocations[l].coords);
    playerToken.targetX.position.set(...coords[coords.length - 1]);
    const lines = buildPathLines(coords.slice(targetLocationRoute.indexOf(store.currentLocation())));
    animateShownPathCallback = (t) => {
      pathProg = (pathProg + t.deltaMS) % CYCLE_DURATION;
      const pathProgFraction = pathProg / CYCLE_DURATION;
      playerToken.pathGfx.context = lines.at(-Math.ceil(pathProgFraction * lines.length)) as unknown as GraphicsContext;
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
          const targetCoords = defs.allLocations[store.nextLocationTowardsTarget()].coords;
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
      queuedActions.enqueue(resolveAction(action));
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

        fullMove.run(1, playerToken, landmarkMarkers, fillerMarkers);
        prog -= moveDuration;
      }

      const nextMove = queuedActions.peek();
      if (nextMove === undefined) {
        const fromCoords = defs.allLocations[store.currentLocation()].coords;
        const toCoords = defs.allLocations[store.nextLocationTowardsTarget()].coords;
        playerToken.sprite.position.set(...fromCoords);
        playerTokenPosition.set(fromCoords);
        if (store.currentLocation() !== store.nextLocationTowardsTarget()) {
          updateWiggleOptimizationBox(fromCoords, toCoords);
        }
        t.remove(animatePlayerMoveCallback);
        animatePlayerMoveCallback = null;
        prog = 0;
        t.speed = 1;
        return;
      }

      nextMove.run(prog / moveDuration, playerToken, landmarkMarkers, fillerMarkers);
    };
    ticker.add(animatePlayerMoveCallback);
  });

  return {
    playerTokenResource,
    landmarksResource,
    fillerMarkersSignal,
  };
}
