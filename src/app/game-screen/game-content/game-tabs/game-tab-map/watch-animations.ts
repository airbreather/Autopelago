import { Dialog } from '@angular/cdk/dialog';
import type { CdkConnectedOverlay } from '@angular/cdk/overlay';
import { DestroyRef, effect, inject, Injector, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import scrollIntoView from 'scroll-into-view-if-needed';
import type { AutopelagoLocation } from '../../../../data/resolved-definitions';
import { GameStore } from '../../../../store/autopelago-store';
import { GameScreenStore } from '../../../../store/game-screen-store';
import { PerformanceInsensitiveAnimatableState } from '../../status-display/performance-insensitive-animatable-state';
import { UWin } from './u-win';

interface WatchAnimationsParams {
  dashedPath: SVGPathElement;
  overlay: CdkConnectedOverlay;
  fadeToBlack: HTMLDivElement;
  playerTokenContainer: HTMLDivElement;
  ratPoisonContainer: HTMLDivElement;
  landmarkContainers: readonly HTMLDivElement[];
  questContainers: readonly HTMLDivElement[];
  fillerSquares: readonly HTMLDivElement[];
  enableTileAnimations: boolean;
  enableRatAnimations: boolean;
}

export function watchAnimations(
  { dashedPath, overlay, fadeToBlack, playerTokenContainer, ratPoisonContainer, landmarkContainers, questContainers, fillerSquares, enableTileAnimations, enableRatAnimations }: WatchAnimationsParams,
) {
  const gameStore = inject(GameStore);
  const gameScreenStore = inject(GameScreenStore);
  const performanceInsensitiveAnimatableState = inject(PerformanceInsensitiveAnimatableState);
  const injector = inject(Injector);
  const destroyRef = inject(DestroyRef);
  const dialog = inject(Dialog);

  // get the initial state
  performanceInsensitiveAnimatableState.apparentCurrentLocation.set(gameStore.currentLocation());
  performanceInsensitiveAnimatableState.applySnapshot(
    performanceInsensitiveAnimatableState.getSnapshot({ gameStore, consumeOutgoingAnimatableActions: false }),
  );

  // animate it all the way up at the body level so that it gets inherited by the dialog container.
  // this means that we need to destroy the animation when it goes out of scope.
  const bodyElement = document.getElementsByClassName('insanely-high-target-for-animated-properties-that-also-need-to-be-inherited-on-dialogs')[0] as HTMLElement;
  const landmarkShake = enableTileAnimations
    ? bodyElement.animate([
        { ['--ap-frame-offset']: 0, easing: 'steps(1)' },
        { ['--ap-frame-offset']: 1, easing: 'steps(1)' },
        { ['--ap-frame-offset']: 0, easing: 'steps(1)' },
      ], { duration: 1000, iterations: Infinity })
    : null;
  if (landmarkShake !== null) {
    destroyRef.onDestroy(() => {
      landmarkShake.cancel();
    });
  }
  const playerWiggle = enableRatAnimations
    ? playerTokenContainer.animate({
        ['--ap-wiggle-amount']: [0, 1, 0, -1, 0],
      }, { duration: 1000, iterations: Infinity })
    : null;

  if (!enableRatAnimations) {
    for (const animateElement of dashedPath.getElementsByTagName('animate')) {
      animateElement.remove();
    }
  }

  let currentTransientAnimations: Animation[] = [];
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

  const movementProps = (allLocations: readonly Readonly<AutopelagoLocation>[], fromLocation: number, toLocation: number) => {
    const [fx, fy] = allLocations[fromLocation].coords;
    const [tx, ty] = allLocations[toLocation].coords;
    let neutralAngle = Math.atan2(ty - fy, tx - fx);
    let scaleX = 1;
    if (Math.abs(neutralAngle) >= Math.PI / 2) {
      neutralAngle -= Math.PI;
      scaleX = -1;
    }
    return {
      fx, fy, tx, ty, neutralAngle, scaleX,
    };
  };

  const checkLocations = (locations: Iterable<number>) => {
    for (const loc of locations) {
      const landmarkContainer = landmarkContainersLookup.get(loc);
      if (landmarkContainer !== undefined) {
        landmarkContainer.style.setProperty('--ap-checked-offset', '0');
        landmarkContainersLookup.delete(loc);
      }

      const questContainer = questContainersLookup.get(loc);
      if (questContainer !== undefined) {
        questContainer.style.display = 'none';
        questContainersLookup.delete(loc);
      }

      const fillerSquare = fillerSquaresLookup.get(loc);
      if (fillerSquare !== undefined) {
        fillerSquare.style.backgroundColor = 'grey';
        fillerSquaresLookup.delete(loc);
      }
    }
  };

  window.setTimeout(() => {
    let immediateDeathCallback: (() => void) | null = null;
    effect(() => {
      const { allLocations } = gameStore.defs();
      const coords = allLocations[untracked(() => gameStore.currentLocation())].coords;
      playerTokenContainer.style.setProperty('--ap-left-base', `${coords[0].toString()}px`);
      playerTokenContainer.style.setProperty('--ap-top-base', `${coords[1].toString()}px`);
      checkLocations(untracked(() => gameStore.checkedLocations()));
    }, { injector });
    effect(() => {
      if (gameStore.running()) {
        playerWiggle?.play();
        landmarkShake?.play();
        currentTransientAnimations.forEach((a) => {
          a.play();
        });
      }
      else {
        playerWiggle?.pause();
        landmarkShake?.pause();
        currentTransientAnimations.forEach((a) => {
          a.pause();
        });
      }
    }, { injector });
    let wasShowingPath = false;
    effect(() => {
      if (!gameScreenStore.showingPath()) {
        if (wasShowingPath) {
          dashedPath.style.display = 'none';
          wasShowingPath = false;
        }

        return;
      }

      const currentLocation = performanceInsensitiveAnimatableState.apparentCurrentLocation();
      const targetLocationRoute = performanceInsensitiveAnimatableState.targetLocationRoute();
      const { allLocations } = gameStore.defs();
      let foundCurrentLocation = false;
      const newData: string[] = [];
      for (let i = 0; i < targetLocationRoute.length; i++) {
        const loc = targetLocationRoute[i];
        const [x, y] = allLocations[loc].coords;
        if (loc === currentLocation) {
          if (i === targetLocationRoute.length - 1) {
            // just a dot, that's not very interesting.
            break;
          }
          foundCurrentLocation = true;
          newData.push(`M${x.toFixed(2)},${y.toFixed(2)}`);
        }
        else if (foundCurrentLocation) {
          newData.push(`L${x.toFixed(2)},${y.toFixed(2)}`);
        }
      }
      if (!foundCurrentLocation) {
        dashedPath.style.display = 'none';
        wasShowingPath = false;
        return;
      }
      dashedPath.style.display = '';
      dashedPath.setAttribute('d', newData.join(' '));
      wasShowingPath = true;
    }, { injector });
    effect(() => {
      const { allLocations, regionForLandmarkLocation } = gameStore.defs();
      // this captures the full snapshot at this time, but applySnapshot only handles some of it.
      const snapshot = performanceInsensitiveAnimatableState.getSnapshot({ gameStore, consumeOutgoingAnimatableActions: true });
      for (const anim of snapshot.outgoingAnimatableActions) {
        switch (anim.type) {
          case 'move': {
            if (anim.fromLocation === anim.toLocation) {
              continue;
            }

            const prevPrevAnimation = prevAnimation;
            prevAnimation = (async () => {
              await prevPrevAnimation;
              if (destroyRef.destroyed) {
                return;
              }
              const { tx, ty, neutralAngle, scaleX } = movementProps(allLocations, anim.fromLocation, anim.toLocation);
              performanceInsensitiveAnimatableState.apparentCurrentLocation.set(anim.toLocation);
              playerTokenContainer.style.setProperty('--ap-neutral-angle', neutralAngle.toString() + 'rad');
              playerTokenContainer.style.setProperty('--ap-scale-x', scaleX.toString());
              const currentAnimation = playerTokenContainer.animate({
                ['--ap-left-base']: [tx.toString() + 'px'],
                ['--ap-top-base']: [ty.toString() + 'px'],
              }, { fill: 'forwards', duration: enableRatAnimations ? 100 : 0 });
              if (!gameStore.running()) {
                currentAnimation.pause();
              }
              currentTransientAnimations = [currentAnimation];
              try {
                await currentAnimation.finished;
                currentAnimation.commitStyles();
                currentAnimation.cancel();
              }
              catch {
                // doesn't matter.
              }
              /*
              eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
              --
              I would LOVE to remove the condition, but this property was declared incorrectly. not
              only CAN it be nullish, but it IS nullish. quite often, in fact.
              */
              overlay.overlayRef?.updatePosition();
              currentTransientAnimations = [];
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

          case 'death': {
            let deathDelay = gameStore.deathDelaySeconds() * 1000;
            if (anim.cause !== 'just-poisoned') {
              if (immediateDeathCallback !== null) {
                // there's already a death animation playing. finish it and let the rest play out.
                immediateDeathCallback();
                break;
              }
              gameStore.killPlayerBegin();
              deathDelay = 0;
            }

            const prevPrevAnimation = prevAnimation;
            const startLocation = gameStore.defs().startLocation;
            const [x, y] = gameStore.defs().allLocations[startLocation].coords;
            prevAnimation = (async () => {
              await prevPrevAnimation;
              if (destroyRef.destroyed) {
                return;
              }
              playerWiggle?.pause();
              const ratLeft = Number(playerTokenContainer.style.getPropertyValue('--ap-left-base').replace('px', ''));
              const ratTop = Number(playerTokenContainer.style.getPropertyValue('--ap-top-base').replace('px', ''));
              const ratLeftTarget = (ratLeft + 150) / 2;
              const poisonLeft = ratLeft > 150 ? 0 : 300;
              const poisonLeftTarget = ((ratLeft + 150) / 2) + (ratLeft > 150 ? -16 : 16);
              const neutralAngleProp = playerTokenContainer.style.getPropertyValue('--ap-neutral-angle');
              const neutralAngleSign = neutralAngleProp.startsWith('-') ? -1 : 1;
              ratPoisonContainer.style.setProperty('display', 'block');
              ratPoisonContainer.style.setProperty('--ap-left-base', `${poisonLeft.toString()}px`);
              ratPoisonContainer.style.setProperty('--ap-top-base', `${ratTop.toString()}px`);
              ratPoisonContainer.style.setProperty('--ap-neutral-angle', '0rad');
              performanceInsensitiveAnimatableState.apparentCurrentLocation.set(startLocation);
              fadeToBlack.style.opacity = '0';
              const animateFadeToBlack = fadeToBlack.animate({
                opacity: [1],
              }, { fill: 'forwards', duration: deathDelay });
              const playerToken = playerTokenContainer.firstElementChild;
              if (playerToken) {
                scrollIntoView(playerToken, { behavior: 'instant', block: 'center', scrollMode: 'if-needed' });
              }
              const animateRat = playerTokenContainer.animate({
                ['--ap-left-base']: `${ratLeftTarget.toString()}px`,
                ['--ap-neutral-angle']: `${(neutralAngleSign * 180).toString()}deg`,
              }, { fill: 'forwards', duration: deathDelay });
              const animatePoison = ratPoisonContainer.animate({
                ['--ap-left-base']: `${poisonLeftTarget.toString()}px`,
                ['--ap-neutral-angle']: ['3600deg'],
              }, { fill: 'forwards', duration: deathDelay });
              currentTransientAnimations = [animateFadeToBlack, animateRat, animatePoison];
              if (!gameStore.running()) {
                currentTransientAnimations.forEach((a) => {
                  a.pause();
                });
              }
              try {
                if (deathDelay > 0) {
                  // allow this await to get interrupted prematurely if a Death Link comes in while
                  // we're in the middle of a death animation. incoming animatable actions can't cut
                  // in line (by design!) because it's all structured as a timeline of sorts (not an
                  // AnimationTimeline), and time only flows forward or pauses. instead, we create a
                  // wormhole through spacetime that a future death animation can jump through where
                  // the only thing it's capable of doing in the past is to tell us to stop early.
                  await Promise.any([
                    Promise.all(currentTransientAnimations.map(a => a.finished)),
                    new Promise<void>(resolve => immediateDeathCallback = resolve),
                  ]);
                  immediateDeathCallback = null;
                }
                fadeToBlack.style.opacity = '1';
                currentTransientAnimations.forEach((a) => {
                  a.commitStyles();
                  a.cancel();
                });
                switch (anim.cause) {
                  case 'just-poisoned':
                    gameStore.killPlayerEnd('{PLAYER_ALIAS} drank poison.');
                    break;

                  case 'death-link':
                    gameStore.killPlayerEnd(null);
                    break;
                }
                ratPoisonContainer.style.setProperty('display', 'none');
                const animateRatBack = playerTokenContainer.animate({
                  ['--ap-left-base']: `${x.toString()}px`,
                  ['--ap-top-base']: `${y.toString()}px`,
                  ['--ap-neutral-angle']: '3600deg',
                }, { fill: 'forwards', duration: 2000 });
                currentTransientAnimations = [animateRatBack];
                await animateRatBack.finished;
                currentTransientAnimations.forEach((a) => {
                  a.commitStyles();
                  a.cancel();
                });
                playerTokenContainer.style.setProperty('--ap-neutral-angle', '0rad');
                playerTokenContainer.style.setProperty('--ap-scale-x', '1');
                fadeToBlack.style.opacity = '0';
                if (gameStore.running()) {
                  playerWiggle?.play();
                }
              }
              catch {
                // doesn't matter.
              }
              /*
              eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
              --
              I would LOVE to remove the condition, but this property was declared incorrectly. not
              only CAN it be nullish, but it IS nullish. quite often, in fact.
              */
              overlay.overlayRef?.updatePosition();
              currentTransientAnimations = [];
            })();
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
