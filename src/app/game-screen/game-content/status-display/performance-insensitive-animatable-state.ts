import { Injectable, signal } from '@angular/core';
import { BAKED_DEFINITIONS_FULL } from '../../../data/resolved-definitions';
import type { GameStore } from '../../../store/autopelago-store';

interface GetSnapshotOptions {
  gameStore: InstanceType<typeof GameStore>;
  consumeOutgoingAnimatableActions: boolean;
}

@Injectable()
export class PerformanceInsensitiveAnimatableState {
  readonly ratCount = signal(0);
  readonly food = signal(0);
  readonly energy = signal(0);
  readonly luck = signal(0);

  readonly distraction = signal(0);
  readonly startled = signal(0);
  readonly smart = signal(false);
  readonly conspiratorial = signal(false);
  readonly stylish = signal(0);
  readonly confidence = signal(false);

  readonly receivedItemCountLookup = signal<readonly number[]>(Array.from({ length: BAKED_DEFINITIONS_FULL.allItems.length }, () => 0));
  readonly allLocationsAreChecked = signal(false);
  readonly hasCompletedGoal = signal(false);

  readonly apparentCurrentLocation = signal(0);
  readonly targetLocation = signal(0);
  readonly targetLocationRoute = signal<readonly number[]>([]);

  getSnapshot({ gameStore, consumeOutgoingAnimatableActions }: GetSnapshotOptions) {
    return {
      ratCount: gameStore.ratCount(),
      food: gameStore.foodFactor(),
      energy: gameStore.energyFactor(),
      luck: gameStore.luckFactor(),
      distraction: gameStore.distractionCounter(),
      startled: gameStore.startledCounter(),
      smart: gameStore.targetLocationChosenBecauseSmart(),
      conspiratorial: gameStore.targetLocationChosenBecauseConspiratorial(),
      stylish: gameStore.styleFactor(),
      confidence: gameStore.hasConfidence(),
      outgoingAnimatableActions: consumeOutgoingAnimatableActions
        ? gameStore.consumeOutgoingAnimatableActions()
        : gameStore.outgoingAnimatableActions(),
      receivedItemCountLookup: gameStore.receivedItemCountLookup(),
      regionIsLandmarkWithRequirementSatisfied: gameStore.regionLocks().regionIsLandmarkWithRequirementSatisfied,
      allLocationsAreChecked: gameStore.allLocationsAreChecked(),
      hasCompletedGoal: gameStore.hasCompletedGoal(),
      targetLocation: gameStore.targetLocation(),
      targetLocationRoute: gameStore.targetLocationRoute(),
    };
  }

  applySnapshot(snapshot: ReturnType<this['getSnapshot']>) {
    const { ratCount, food, energy, luck, distraction, startled, smart, conspiratorial, stylish, confidence, receivedItemCountLookup, allLocationsAreChecked, hasCompletedGoal, targetLocation, targetLocationRoute } = snapshot;
    this.ratCount.set(ratCount);
    this.food.set(food);
    this.energy.set(energy);
    this.luck.set(luck);
    this.distraction.set(distraction);
    this.startled.set(startled);
    this.smart.set(smart);
    this.conspiratorial.set(conspiratorial);
    this.stylish.set(stylish);
    this.confidence.set(confidence);
    this.receivedItemCountLookup.set(receivedItemCountLookup);
    this.allLocationsAreChecked.set(allLocationsAreChecked);
    this.hasCompletedGoal.set(hasCompletedGoal);
    this.targetLocation.set(targetLocation);
    this.targetLocationRoute.set(targetLocationRoute);
  }
}
