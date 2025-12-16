import { Injectable, signal } from '@angular/core';
import { BAKED_DEFINITIONS_FULL } from '../../data/resolved-definitions';

@Injectable()
export class AnimationSequencedGameState {
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
  readonly locationIsChecked = Array.from({ length: BAKED_DEFINITIONS_FULL.allLocations.length + 1 }, () => signal(false));
}
