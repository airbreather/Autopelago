import { Injectable, signal } from '@angular/core';
import { BAKED_DEFINITIONS_FULL } from '../../../data/resolved-definitions';
import type { GameStore } from '../../../store/autopelago-store';

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

  readonly itemCount = Array.from({ length: BAKED_DEFINITIONS_FULL.allItems.length }, () => signal(0));

  getSnapshot(gameStore: InstanceType<typeof GameStore>) {
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
      outgoingAnimatableActions: gameStore.consumeOutgoingAnimatableActions(),
      receivedItemCountLookup: gameStore.receivedItemCountLookup(),
      checkedLocations: gameStore.checkedLocations(),
      regionIsLandmarkWithRequirementSatisfied: gameStore.regionLocks().regionIsLandmarkWithRequirementSatisfied,
    };
  }

  applySnapshot(snapshot: ReturnType<this['getSnapshot']>) {
    const { ratCount, food, energy, luck, distraction, startled, smart, conspiratorial, stylish, confidence, receivedItemCountLookup } = snapshot;
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
    for (let i = 0; i < receivedItemCountLookup.length; ++i) {
      this.itemCount[i].set(receivedItemCountLookup[i]);
    }
  }
}
