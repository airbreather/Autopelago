import { Dialog } from '@angular/cdk/dialog';
import { DestroyRef, effect, type Injector, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { GameStore } from '../../../../store/autopelago-store';
import type { GameScreenStore } from '../../../../store/game-screen-store';
import { PerformanceInsensitiveAnimatableState } from '../../status-display/performance-insensitive-animatable-state';
import { UWin } from './u-win';

interface WatchAnimationsParams {
  gameStore: InstanceType<typeof GameStore>;
  gameScreenStore: InstanceType<typeof GameScreenStore>;
  outerDiv: HTMLDivElement;
  mapCanvas: HTMLCanvasElement;
  playerTokenContainer: HTMLDivElement;
  landmarkContainers: readonly HTMLDivElement[];
  questContainers: readonly HTMLDivElement[];
  fillerSquares: readonly HTMLDivElement[];
  performanceInsensitiveAnimatableState: PerformanceInsensitiveAnimatableState;
  injector: Injector;
}

export function watchAnimations(
  { gameStore, gameScreenStore, outerDiv, mapCanvas, playerTokenContainer, landmarkContainers, questContainers, fillerSquares, performanceInsensitiveAnimatableState, injector }: WatchAnimationsParams,
) {
  const destroyRef = injector.get(DestroyRef);
  const dialog = injector.get(Dialog);
  const landmarkShake = outerDiv.animate([
    { ['--ap-frame-offset']: 0, easing: 'steps(1)' },
    { ['--ap-frame-offset']: 1, easing: 'steps(1)' },
    { ['--ap-frame-offset']: 0, easing: 'steps(1)' },
  ], { duration: 1000, iterations: Infinity });
  const playerWiggle = playerTokenContainer.animate({
    ['--ap-wiggle-amount']: [0, 1, 0, -1, 0],
  }, { duration: 1000, iterations: Infinity });

  let currentMovementAnimation: Animation | null = null;

  let prevAnimation = Promise.resolve();

  const landmarkContainersLookup = new Map<number, HTMLDivElement>();
  for (const landmarkContainer of landmarkContainers) {
    landmarkContainersLookup.set(Number(landmarkContainer.dataset['locationId']), landmarkContainer);
  }

  const questContainersLookup = new Map<number, HTMLDivElement>();
  for (const questContainer of questContainers) {
    questContainersLookup.set(Number(questContainer.dataset['locationId']), questContainer);
  }

  const fillerSquaresLookup = new Map<number, HTMLDivElement>();
  for (const fillerSquare of fillerSquares) {
    fillerSquaresLookup.set(Number(fillerSquare.dataset['locationId']), fillerSquare);
  }

  const checkLocations = (locations: Iterable<number>) => {
    for (const loc of locations) {
      const landmarkContainer = landmarkContainersLookup.get(loc);
      if (landmarkContainer !== undefined) {
        landmarkContainer.style.setProperty('--ap-checked-offset', '0');
        landmarkContainersLookup.delete(loc);
      }

      const questContainer = questContainersLookup.get(loc);
      if (questContainer !== undefined) {
        questContainer.style.setProperty('display', 'none');
        questContainersLookup.delete(loc);
      }

      const fillerSquare = fillerSquaresLookup.get(loc);
      if (fillerSquare !== undefined) {
        fillerSquare.style.setProperty('background-color', 'grey');
        fillerSquaresLookup.delete(loc);
      }
    }
  };

  setTimeout(() => {
    effect(() => {
      const { allLocations } = gameStore.defs();
      const coords = allLocations[untracked(() => gameStore.currentLocation())].coords;
      playerTokenContainer.style.setProperty('--ap-left-base', `${coords[0].toString()}px`);
      playerTokenContainer.style.setProperty('--ap-top-base', `${coords[1].toString()}px`);
      checkLocations(untracked(() => gameStore.checkedLocations()));
    }, { injector });
    effect(() => {
      if (gameStore.running()) {
        playerWiggle.play();
        landmarkShake.play();
        currentMovementAnimation?.play();
      }
      else {
        playerWiggle.pause();
        landmarkShake.pause();
        currentMovementAnimation?.pause();
      }
    }, { injector });
    let wasShowingPath: boolean | null = null;
    const apparentCurrentLocation = signal(0);
    let lastAnimationFrame: number | null = null;
    effect(() => {
      if (lastAnimationFrame !== null) {
        cancelAnimationFrame(lastAnimationFrame);
        lastAnimationFrame = null;
      }

      if (!gameScreenStore.showingPath()) {
        if (wasShowingPath) {
          const ctx = mapCanvas.getContext('2d');
          if (ctx !== null) {
            ctx.clearRect(0, 0, mapCanvas.width, mapCanvas.height);
          }
          wasShowingPath = false;
        }

        return;
      }

      const targetLocationRoute = performanceInsensitiveAnimatableState.targetLocationRoute();
      const ctx = mapCanvas.getContext('2d');
      if (ctx === null) {
        return;
      }
      ctx.clearRect(0, 0, mapCanvas.width, mapCanvas.height);
      const scale = mapCanvas.width / 300;
      const path = new Path2D();
      const { allLocations } = gameStore.defs();
      const currentLocation = apparentCurrentLocation();
      let foundCurrentLocation = false;
      for (let i = 0; i < targetLocationRoute.length; i++) {
        const loc = targetLocationRoute[i];
        const [x, y] = allLocations[loc].coords;
        if (loc === currentLocation) {
          if (i === targetLocationRoute.length - 1) {
            // just a dot, that's not very interesting.
            break;
          }
          foundCurrentLocation = true;
          path.moveTo(x * scale, y * scale);
        }
        else if (foundCurrentLocation) {
          path.lineTo(x * scale, y * scale);
        }
      }
      if (!foundCurrentLocation) {
        return;
      }

      ctx.strokeStyle = 'red';
      ctx.lineWidth = scale;
      ctx.setLineDash([scale * 2, scale]);
      ctx.clearRect(0, 0, mapCanvas.width, mapCanvas.height);
      ctx.lineDashOffset = 0;
      ctx.stroke(path);
    }, { injector });
    effect(() => {
      const { allLocations, regionForLandmarkLocation } = gameStore.defs();
      // this captures the full snapshot at this time, but applySnapshot only handles some of it.
      const snapshot = performanceInsensitiveAnimatableState.getSnapshot(gameStore);
      for (const anim of snapshot.outgoingAnimatableActions) {
        switch (anim.type) {
          case 'move': {
            if (anim.fromLocation === anim.toLocation) {
              continue;
            }

            const [fx, fy] = allLocations[anim.fromLocation].coords;
            const [tx, ty] = allLocations[anim.toLocation].coords;
            let neutralAngle = Math.atan2(ty - fy, tx - fx);
            let scaleX = 1;
            if (Math.abs(neutralAngle) >= Math.PI / 2) {
              neutralAngle -= Math.PI;
              scaleX = -1;
            }
            const prevPrevAnimation = prevAnimation;
            prevAnimation = (async () => {
              await prevPrevAnimation;
              if (destroyRef.destroyed) {
                return;
              }
              apparentCurrentLocation.set(anim.toLocation);
              playerTokenContainer.style.setProperty('--ap-neutral-angle', neutralAngle.toString() + 'rad');
              playerTokenContainer.style.setProperty('--ap-scale-x', scaleX.toString());
              currentMovementAnimation = playerTokenContainer.animate({
                ['--ap-left-base']: [tx.toString() + 'px'],
                ['--ap-top-base']: [ty.toString() + 'px'],
              }, { fill: 'forwards', duration: 100 });
              try {
                await currentMovementAnimation.finished;
                currentMovementAnimation.commitStyles();
                currentMovementAnimation.cancel();
              }
              catch {
                // doesn't matter.
              }
              currentMovementAnimation = null;
            })();
            break;
          }

          case 'check-locations': {
            const prevPrevAnimation = prevAnimation;
            prevAnimation = (async () => {
              await prevPrevAnimation;
              if (destroyRef.destroyed) {
                return;
              }
              checkLocations(anim.locations);
            })();
            break;
          }

          case 'u-win': {
            const prevPrevAnimation = prevAnimation;
            prevAnimation = (async () => {
              await prevPrevAnimation;
              if (destroyRef.destroyed) {
                return;
              }
              const dialogRef = dialog.open(UWin, {
                width: '60%',
                height: '60%',
              });
              const closeOnDestroy = destroyRef.onDestroy(() => {
                sub.unsubscribe();
                dialogRef.close();
              });
              const sub = dialogRef.closed
                .pipe(takeUntilDestroyed(destroyRef))
                .subscribe(() => {
                  closeOnDestroy();
                });
            })();
            break;
          }
        }
      }

      // handle the rest of the stuff that happened on the turn in question. for the most part, it's
      // better to do this stuff later because, e.g., if you get startled for one turn, then all the
      // movement you do on the startled turn happens first, THEN the startled counter decrements.
      {
        const prevPrevAnimation = prevAnimation;
        prevAnimation = (async () => {
          await prevPrevAnimation;
          if (destroyRef.destroyed) {
            return;
          }
          performanceInsensitiveAnimatableState.applySnapshot(snapshot);

          // applySnapshot doesn't do quest markers
          for (const [loc, questContainer] of questContainersLookup.entries()) {
            const region = regionForLandmarkLocation[loc];
            if (snapshot.regionIsLandmarkWithRequirementSatisfied[region]) {
              questContainer.style.setProperty('--ap-blocked-offset', '0');
            }
          }
        })();
      }
    }, { injector });
  });
}
