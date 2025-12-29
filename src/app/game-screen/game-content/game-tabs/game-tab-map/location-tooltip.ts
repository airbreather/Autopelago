import { Hint, Player } from '@airbreather/archipelago.js';
import { Component, computed, inject, input } from '@angular/core';
import { LANDMARKS } from '../../../../data/locations';
import type { AutopelagoLandmarkRegion } from '../../../../data/resolved-definitions';
import { GameStore } from '../../../../store/autopelago-store';
import { RequirementDisplay } from './requirement-display';

@Component({
  selector: 'app-location-tooltip',
  imports: [
    RequirementDisplay,
  ],
  template: `
    <div class="outer">
      <h1 class="box header">{{ location().name }}<span
        class="hyper-focus-help">{{ isHyperFocusLocation() ? '*' : '' }}</span></h1>
      @if (landmarkRegion(); as region) {
        <div class="box main-content">
          <div class="image-and-requirement">
            <!--suppress CheckImageSize -->
            <img class="landmark-image" [alt]="region.yamlKey"
                 width="64" height="64"
                 [style.--ap-sprite-index]="spriteIndex()"
                 src="/assets/images/locations.webp">
            <app-requirement-display class="requirement-display" [requirement]="region.requirement"/>
          </div>
          <div class="flavor-text" [hidden]="!location().flavorText">“{{ location().flavorText }}”</div>
        </div>
      }

      @if (isHyperFocusLocation()) {
        <div class="box hyper-focus">
          <span class="hyper-focus-help">*</span>The rat will try as hard as it can to get here.
        </div>
      }

      @if (hint(); as hint) {
        @let hintedItem = hint.item;
        <div class="box hint">
          <span class="player-text" [class.own-player-text]="isSelf(hintedItem.receiver)">{{ hintedItem.receiver }}</span>'s
          <span class="item-text" [class.progression]="hintedItem.progression" [class.filler]="hintedItem.filler"
                [class.useful]="hintedItem.useful" [class.trap]="hintedItem.trap">
            {{ hintedItem }}
          </span> is here (<span
            class="hint-text"
            [class.unspecified]="hint.status === HINT_STATUS_UNSPECIFIED"
            [class.no-priority]="hint.status === HINT_STATUS_NO_PRIORITY"
            [class.avoid]="hint.status === HINT_STATUS_AVOID"
            [class.priority]="hint.status === HINT_STATUS_PRIORITY"
            [class.found]="hint.status === HINT_STATUS_FOUND"
        >{{ hintStatusText() }}</span>).
        </div>
      }
    </div>
  `,
  styles: `
    @use '../../../../../theme';

    .outer {
      display: grid;
      grid-template-columns: min-content;
      grid-auto-rows: auto;
      gap: 5px;
      padding: 4px;
      background-color: theme.$region-color;
    }

    .box {
      padding: 4px;
      border: 2px solid black;
    }

    .header {
      margin: 0;
      font-size: 14pt;
      white-space: nowrap;
    }

    .image-and-requirement {
      display: grid;
      grid-template-columns: auto 1fr;
      align-items: start;
    }

    .landmark-image {
      object-fit: none;
      object-position: calc(-65px * var(--ap-frame-offset, 0)) calc(-65px * var(--ap-sprite-index, 0));
    }

    .requirement-display {
      margin-left: 5px;
      white-space: nowrap;
    }

    .flavor-text {
      margin-top: 10px;
      font-size: 8pt;
    }

    .hyper-focus {
      font-size: 8pt;
      white-space: nowrap;
    }

    .hyper-focus-help {
      color: red;
    }

    .hint {
      font-size: 8pt;
    }
  `,
})
export class LocationTooltip {
  readonly #store = inject(GameStore);
  readonly locationKey = input.required<number>();
  protected readonly isHyperFocusLocation = computed(() => this.#store.hyperFocusLocation() === this.locationKey());
  protected readonly location = computed(() => this.#store.defs().allLocations[this.locationKey()]);
  protected readonly hint = computed(() => this.#store.game()?.hintedLocations().get(this.locationKey()) ?? null);
  protected readonly HINT_STATUS_UNSPECIFIED: Hint['status'] = 0;
  protected readonly HINT_STATUS_NO_PRIORITY: Hint['status'] = 10;
  protected readonly HINT_STATUS_AVOID: Hint['status'] = 20;
  protected readonly HINT_STATUS_PRIORITY: Hint['status'] = 30;
  protected readonly HINT_STATUS_FOUND: Hint['status'] = 40;
  protected readonly hintStatusText = computed(() => {
    // https://github.com/ArchipelagoMW/Archipelago/blob/0.6.5/kvui.py#L1195-L1201
    switch (this.hint()?.status) {
      case this.HINT_STATUS_FOUND: return 'Found';
      case this.HINT_STATUS_UNSPECIFIED: return 'Unspecified';
      case this.HINT_STATUS_NO_PRIORITY: return 'No Priority';
      case this.HINT_STATUS_AVOID: return 'Avoid';
      case this.HINT_STATUS_PRIORITY: return 'Priority';
      default: return null;
    }
  });

  protected readonly landmarkRegion = computed(() => {
    const { allRegions, regionForLandmarkLocation } = this.#store.defs();
    const regionKeyIfLandmark = regionForLandmarkLocation[this.locationKey()];
    return Number.isNaN(regionKeyIfLandmark)
      ? null
      : allRegions[regionKeyIfLandmark] as AutopelagoLandmarkRegion;
  });

  protected readonly spriteIndex = computed(() => LANDMARKS[this.landmarkRegion()?.yamlKey ?? 'snakes_on_a_planet'].sprite_index);

  protected isSelf(player: Player) {
    const game = this.#store.game();
    if (game === null) {
      return false;
    }

    const { team, slot } = game.client.players.self;
    return player.team === team && player.slot === slot;
  }
}
