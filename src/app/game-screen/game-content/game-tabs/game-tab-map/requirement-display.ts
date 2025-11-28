import { Component, computed, inject, input } from '@angular/core';
import type { AutopelagoRequirement } from '../../../../data/resolved-definitions';
import { buildRequirementIsSatisfied } from '../../../../game/location-routing';
import { GameStore } from '../../../../store/autopelago-store';

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
        <span>{{itemNames()[r.item]}}</span>
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
  readonly #store = inject(GameStore);
  readonly requirement = input.required<AutopelagoRequirement>();

  readonly itemNames = computed(() => {
    const { allItems } = this.#store.defs();
    const lactoseIntolerant = this.#store.lactoseIntolerant();
    return allItems.map(i => lactoseIntolerant ? i.lactoseIntolerantName : i.lactoseName);
  });

  readonly isSatisfied = computed(() => {
    const isSatisfied = buildRequirementIsSatisfied(this.#store.requirementRelevantItemCountLookup(), this.#store.allLocationsAreChecked());
    return isSatisfied(this.requirement());
  });
}
