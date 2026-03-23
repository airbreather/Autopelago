import { Component, computed, inject, input } from '@angular/core';
import type { AutopelagoUniqueItemKey } from '../../../../data/items';
import { type AutopelagoRequirement, BAKED_DEFINITIONS_FULL } from '../../../../data/resolved-definitions';
import { buildRequirementIsSatisfied } from '../../../../game/location-routing';
import { PerformanceInsensitiveAnimatableState } from '../../status-display/performance-insensitive-animatable-state';

@Component({
  selector: 'app-requirement-display',
  imports: [],
  template: `
    <div class="outer">
      @let r = requirement();
      <span class="satisfied-marker" [hidden]="'fullClear' in r">{{isSatisfied() ? '✓' : '✕'}}</span>
      @if ('ratCount' in r) {
        <span>{{r.ratCount}} rats</span>
      }
      @else if ('item' in r) {
        <span>{{itemName(r.item)}}</span>
      }
      @else if ('fullClear' in r) {
      }
      @else {
        @switch (r.minRequired) {
          @case (1) {
            <span>Any:</span>
          }
          @case (2) {
            <span>Any 2:</span>
          }
          @default {
            <span>All:</span>
          }
        }
        @for (child of r.children; track $index) {
          <div class="child">
            <app-requirement-display [requirement]="child" />
          </div>
        }
      }
    </div>
  `,
  styles: `
    .outer {
      font-size: 8pt;
    }
    .satisfied-marker {
      margin-right: 5px;
    }
    .child {
      margin-left: 15px;
    }
  `,
})
export class RequirementDisplay {
  readonly #anim = inject(PerformanceInsensitiveAnimatableState);
  readonly requirement = input.required<AutopelagoRequirement>();

  protected readonly isSatisfied = computed(() => {
    const isSatisfied = buildRequirementIsSatisfied(this.#anim.receivedUniqueItems(), this.#anim.ratCount(), this.#anim.allLocationsAreChecked());
    return isSatisfied(this.requirement());
  });

  protected itemName(key: AutopelagoUniqueItemKey) {
    const baked = BAKED_DEFINITIONS_FULL.uniqueItemsByYamlKey.get(key);
    return baked?.name ?? 'Unknown Item';
  }
}
