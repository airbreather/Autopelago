import { DestroyRef, effect, type Injector, untracked } from '@angular/core';
import type { GameStore } from '../../../../store/autopelago-store';
import { PerformanceInsensitiveAnimatableState } from '../../status-display/performance-insensitive-animatable-state';

interface WatchAnimationsParams {
  gameStore: InstanceType<typeof GameStore>;
  playerTokenContainer: HTMLDivElement;
  landmarkContainers: readonly HTMLDivElement[];
  questContainers: readonly HTMLDivElement[];
  fillerSquares: readonly HTMLDivElement[];
  performanceInsensitiveAnimatableState: PerformanceInsensitiveAnimatableState;
  injector: Injector;
}

export function watchAnimations(
  { gameStore, playerTokenContainer, landmarkContainers, questContainers, fillerSquares, performanceInsensitiveAnimatableState, injector }: WatchAnimationsParams,
) {
  const destroyRef = injector.get(DestroyRef);
  playerTokenContainer.animate({
    ['--ap-wiggle-amount']: [0, 1, 0, -1, 0],
  }, { duration: 1000, iterations: Infinity });

  let prevAnimation = Promise.resolve();

  const landmarkContainersLookup = new Map<number, HTMLDivElement>();
  for (const landmarkContainer of landmarkContainers) {
    landmarkContainersLookup.set(landmarkContainer.dataset['locationId'] as unknown as number, landmarkContainer);
  }

  const questContainersLookup = new Map<number, HTMLDivElement>();
  for (const questContainer of questContainers) {
    questContainersLookup.set(questContainer.dataset['locationId'] as unknown as number, questContainer);
  }

  const fillerSquaresLookup = new Map<number, HTMLDivElement>();
  for (const fillerSquare of fillerSquares) {
    fillerSquaresLookup.set(fillerSquare.dataset['locationId'] as unknown as number, fillerSquare);
  }

  setTimeout(() => {
    effect(() => {
      const { allLocations } = gameStore.defs();
      const coords = allLocations[untracked(() => gameStore.currentLocation())].coords;
      playerTokenContainer.style.setProperty('--ap-left-base', `${coords[0].toString()}px`);
      playerTokenContainer.style.setProperty('--ap-top-base', `${coords[1].toString()}px`);
    }, { injector });
    effect(() => {
      const { allLocations } = gameStore.defs();
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

          // applySnapshot doesn't set the landmarks / fillers. those don't need an animation, so
          // we can just set them inline here.

        })();
      }

      // applySnapshot doesn't do the movements.
      for (const move of snapshot.outgoingMovementActions) {
        if (move.type === 'move') {
          if (move.fromLocation === move.toLocation) {
            continue;
          }

          const [fx, fy] = allLocations[move.fromLocation].coords;
          const [tx, ty] = allLocations[move.toLocation].coords;
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
            const animation = playerTokenContainer.animate({
              ['--ap-left-base']: [tx.toString() + 'px'],
              ['--ap-top-base']: [ty.toString() + 'px'],
            }, { fill: 'forwards', duration: 100 });
            try {
              await animation.finished;
              animation.commitStyles();
              animation.cancel();
            }
            catch {
              // doesn't matter.
            }
          })();
        }
      }
    }, { injector });
  });
}
