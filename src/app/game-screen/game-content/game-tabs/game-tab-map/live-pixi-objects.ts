import { computed, effect, untracked } from '@angular/core';
import { Sprite, type Ticker } from 'pixi.js';
import Queue from 'yocto-queue';
import type { LandmarkYamlKey, Vec2 } from '../../../../data/locations';
import type { AutopelagoDefinitions } from '../../../../data/resolved-definitions';
import type { AnimatableAction } from '../../../../game/defining-state';
import { GameStore } from '../../../../store/autopelago-store';
import { arraysEqual, bitArraysEqual } from '../../../../utils/equal-helpers';
import { createFillerMarkers, type FillerMarkers } from './filler-markers';
import { createLandmarkMarkers, type LandmarkMarkers } from './landmark-markers';
import { createPlayerToken, SCALE, type WiggleOptimizationBox } from './player-token';

interface ResolvedAction {
  run(fraction: number, defs: AutopelagoDefinitions, playerToken: Sprite, landmarkMarkers: LandmarkMarkers, fillerMarkers: FillerMarkers): void;
}

const NO_ACTION = { run: () => { /* empty */ } } as const;
export function createLivePixiObjects(store: InstanceType<typeof GameStore>, ticker: Ticker) {
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

  const playerTokenResource = createPlayerToken({
    ticker,
    wiggleOptimizationBox,
    game: store.game,
    defs: store.defs,
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
        run: (fraction: number, _defs: AutopelagoDefinitions, playerToken: Sprite) => {
          const x = fromCoords[0] + dx * fraction;
          const y = fromCoords[1] + dy * fraction;
          playerToken.position.set(x, y);
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
        run: (fraction: number, _defs: AutopelagoDefinitions, _playerToken: Sprite, landmarkMarkers: LandmarkMarkers, fillerMarkers: FillerMarkers) => {
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

    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    if (action.type === 'completed-goal') {
      return {
        run: (fraction: number, _defs: AutopelagoDefinitions, _playerToken: Sprite, landmarkMarkers: LandmarkMarkers) => {
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

    return NO_ACTION;
  }

  let finishedFirstRound = false;
  let moveDuration = 100;

  let prog = 0;
  const queuedActions = new Queue<ResolvedAction>();
  let animatePlayerMoveCallback: ((t: Ticker) => void) | null = null;
  effect(() => {
    const game = store.game();
    const playerToken = playerTokenResource.value();
    const landmarkMarkers = landmarksResource.value();
    if (!(game && playerToken && landmarkMarkers)) {
      return;
    }

    const fillerMarkers = fillerMarkersSignal();
    const defs = store.defs();
    const actions = store.consumeOutgoingAnimatableActions();
    if (actions.size === 0) {
      if (!finishedFirstRound) {
        untracked(() => {
          const currentCoords = defs.allLocations[store.currentLocation()].coords;
          const targetCoords = defs.allLocations[store.targetLocation()].coords;
          playerToken.position.set(...currentCoords);
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

        fullMove.run(1, defs, playerToken, landmarkMarkers, fillerMarkers);
        prog -= moveDuration;
      }

      const nextMove = queuedActions.peek();
      if (nextMove === undefined) {
        const fromCoords = defs.allLocations[store.currentLocation()].coords;
        const toCoords = defs.allLocations[store.targetLocation()].coords;
        playerToken.position.set(...fromCoords);
        if (store.currentLocation() !== store.targetLocation()) {
          updateWiggleOptimizationBox(fromCoords, toCoords);
        }
        t.remove(animatePlayerMoveCallback);
        animatePlayerMoveCallback = null;
        prog = 0;
        t.speed = 1;
        return;
      }

      nextMove.run(prog / moveDuration, defs, playerToken, landmarkMarkers, fillerMarkers);
    };
    ticker.add(animatePlayerMoveCallback);
  });

  return {
    playerTokenResource,
    landmarksResource,
    fillerMarkersSignal,
  };
}
