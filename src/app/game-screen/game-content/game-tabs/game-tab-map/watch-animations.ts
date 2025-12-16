import { Dialog } from '@angular/cdk/dialog';
import { DestroyRef, effect, type Injector, untracked } from '@angular/core';
import type { GameStore } from '../../../../store/autopelago-store';
import { PerformanceInsensitiveAnimatableState } from '../../status-display/performance-insensitive-animatable-state';
import { UWin } from './u-win';

interface WatchAnimationsParams {
  gameStore: InstanceType<typeof GameStore>;
  outerDiv: HTMLDivElement;
  playerTokenContainer: HTMLDivElement;
  landmarkContainers: readonly HTMLDivElement[];
  questContainers: readonly HTMLDivElement[];
  fillerSquares: readonly HTMLDivElement[];
  performanceInsensitiveAnimatableState: PerformanceInsensitiveAnimatableState;
  injector: Injector;
}

export function watchAnimations(
  { gameStore, outerDiv, playerTokenContainer, landmarkContainers, questContainers, fillerSquares, performanceInsensitiveAnimatableState, injector }: WatchAnimationsParams,
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
    effect(() => {
      const { allLocations, regionForLandmarkLocation } = gameStore.defs();
      // this captures the full snapshot at this time, but applySnapshot only handles some of it.
      const snapshot = performanceInsensitiveAnimatableState.getSnapshot(gameStore);
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

      // applySnapshot doesn't do the movements.
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
              dialog.open(UWin, {
                width: '60%',
                height: '60%',
              });
            })();
            break;
          }
        }
      }
    }, { injector });
  });
}
