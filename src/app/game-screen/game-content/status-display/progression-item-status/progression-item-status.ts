import { Hint, Player } from '@airbreather/archipelago.js';
import { Dialog } from '@angular/cdk/dialog';
import { CdkConnectedOverlay, createFlexibleConnectedPositionStrategy } from '@angular/cdk/overlay';
import { ChangeDetectionStrategy, Component, computed, inject, Injector, signal, type Signal } from '@angular/core';
import { List, Repeat } from 'immutable';
import { PROGRESSION_ITEMS_BY_VICTORY_LOCATION } from '../../../../data/items';
import { BAKED_DEFINITIONS_FULL } from '../../../../data/resolved-definitions';
import { GameStore } from '../../../../store/autopelago-store';
import { createEmptyTooltipContext, TooltipBehavior, type TooltipOriginProps } from '../../../../tooltip-behavior';
import { PerformanceInsensitiveAnimatableState } from '../performance-insensitive-animatable-state';

import { RequestHint } from './request-hint';

@Component({
  selector: 'app-progression-item-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CdkConnectedOverlay,
    TooltipBehavior,
  ],
  template: `
    <div #outer class="outer">
      @for (item of items(); track item.name) {
        <div #thisContainer class="item-container" [class.collected]="item.collected()"
             [tabindex]="$index + 500" (click)="onClickItem(item, thisContainer)"
             (keyup.enter)="onClickItem(item, thisContainer)" (keyup.space)="onClickItem(item, thisContainer)"
             appTooltip [tooltipContext]="tooltipContext" (tooltipOriginChange)="setTooltipOrigin($index, item.id, $event, true)">
          <!--suppress AngularNgOptimizedImage -->
          <img class="item"
               [class.rats]="item.id === 'rats'"
               [src]="item.id === 'rats' ? '/assets/images/players/pack_rat.webp' : '/assets/images/items.webp'"
               [alt]="item.name"
               [style.--ap-object-position]="-item.offsetX() + 'px ' + -item.offsetY + 'px'">
          @if (item.id === 'rats') {
            <span class="rat-count-corner">{{ ratCount() }}</span>
          }
        </div>
      }
    </div>
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="tooltipOrigin()?.element ?? outer"
      [cdkConnectedOverlayOpen]="tooltipOrigin() !== null"
      [cdkConnectedOverlayUsePopover]="'inline'"
      (detach)="setTooltipOrigin(0, 0, null, false)">
      @if (tooltipOrigin(); as origin) {
        @let item = items()[origin.index];
        <div
          class="tooltip"
          (mouseenter)="tooltipContext.notifyMouseEnterTooltip(origin.uid)"
          (mouseleave)="tooltipContext.notifyMouseLeaveTooltip(origin.uid)"
        >
          <h1 class="box header">{{item.name}}</h1>
          <div class="box flavor-text" [hidden]="!item.flavorText">“{{item.flavorText}}”</div>
          @if (item.id === 'rats') {
            @if (ratHints().size > 0) {
              <div class="box hint rat-hint">
                Hints:
                <ul>
                  @for (hint of ratHints(); track $index) {
                    <li>
                      <span class="item-text" [class.progression]="hint.item.progression" [class.filler]="hint.item.filler"
                            [class.useful]="hint.item.useful" [class.trap]="hint.item.trap">
                        {{ hint.item.name }}
                      </span>
                      at
                      <span class="location-text">{{ hint.item.locationName }}</span>
                      in
                      <span class="player-text" [class.own-player-text]="isSelf(hint.item.sender)">{{ hint.item.sender }}</span>'s world (<span
                        class="hint-text"
                        [class.unspecified]="hint.status === HINT_STATUS_UNSPECIFIED"
                        [class.no-priority]="hint.status === HINT_STATUS_NO_PRIORITY"
                        [class.avoid]="hint.status === HINT_STATUS_AVOID"
                        [class.priority]="hint.status === HINT_STATUS_PRIORITY"
                        [class.found]="hint.status === HINT_STATUS_FOUND"
                      >{{ statusText(hint) }}</span>).
                    </li>
                  }
                </ul>
              </div>
            }
          }
          @else if (hintForTooltipItem(); as hint) {
            <div class="box hint">
              At
              <span class="location-text">{{ hint.item.locationName }}</span>
              in
              <span class="player-text" [class.own-player-text]="isSelf(hint.item.sender)">{{ hint.item.sender }}</span>'s world (<span
                class="hint-text"
                [class.unspecified]="hint.status === HINT_STATUS_UNSPECIFIED"
                [class.no-priority]="hint.status === HINT_STATUS_NO_PRIORITY"
                [class.avoid]="hint.status === HINT_STATUS_AVOID"
                [class.priority]="hint.status === HINT_STATUS_PRIORITY"
                [class.found]="hint.status === HINT_STATUS_FOUND"
              >{{ statusText(hint) }}</span>).
            </div>
          }
        </div>
      }
    </ng-template>
  `,
  styles: `
    @use '../../../../../theme.scss';
    .outer {
      display: flex;
      flex-wrap: wrap;
    }

    .item-container {
      margin: 5px;
      padding: 5px;
      display: inline-grid;

      border: 2px solid black;
      border-radius: 8px;

      &.collected {
        border: 2px solid #FFCE00;
      }
    }

    .item {
      grid-row: 1;
      grid-column: 1;
      width: 64px;
      height: 64px;
      &:not(.rats) {
        object-fit: none;
        object-position: var(--ap-object-position);
      }
    }

    .rat-count-corner {
      grid-row: 1;
      grid-column: 1;
      align-self: end;
      justify-self: end;
    }

    .box {
      padding: 4px;
      border: 2px solid black;
    }

    .tooltip {
      display: grid;
      grid-template-columns: min-content;
      grid-auto-rows: auto;
      gap: 10px;
      padding: 4px;
      background-color: theme.$region-color;
      pointer-events: initial;

      .header {
        margin: 0;
        font-size: 14pt;
        min-width: 300px;
        white-space: nowrap;
      }

      .flavor-text {
        font-size: 8pt;
      }

      .hint {
        font-size: 8pt;
      }

      .rat-hint {
        white-space: nowrap;
      }
    }
  `,
})
export class ProgressionItemStatus {
  readonly #injector = inject(Injector);
  readonly #dialog = inject(Dialog);
  readonly #gameStore = inject(GameStore);
  readonly #performanceInsensitiveAnimatableState = inject(PerformanceInsensitiveAnimatableState);
  protected readonly ratCount = this.#performanceInsensitiveAnimatableState.ratCount.asReadonly();
  protected readonly items: Signal<readonly ItemModel[]>;
  readonly #tooltipOrigin = signal<CurrentTooltipOriginProps | null>(null);
  protected readonly tooltipOrigin = this.#tooltipOrigin.asReadonly();
  // all tooltips here should use the same context, so that the user can quickly switch between them
  // without having to sit through the whole delay.
  protected readonly tooltipContext = createEmptyTooltipContext();
  readonly #hintedItems = computed(() => this.#gameStore.game()?.hintedItems() ?? List(Repeat(null, BAKED_DEFINITIONS_FULL.allItems.length)));
  protected readonly ratHints = computed(() => this.#gameStore.game()?.ratHints() ?? List<Hint>());
  protected readonly hintForTooltipItem = computed(() => {
    const item = this.tooltipOrigin()?.item ?? null;
    const hintedItems = [...this.#hintedItems()];
    return item === null || item === 'rats'
      ? null
      : hintedItems[item] ?? null;
  });

  protected readonly HINT_STATUS_UNSPECIFIED: Hint['status'] = 0;
  protected readonly HINT_STATUS_NO_PRIORITY: Hint['status'] = 10;
  protected readonly HINT_STATUS_AVOID: Hint['status'] = 20;
  protected readonly HINT_STATUS_PRIORITY: Hint['status'] = 30;
  protected readonly HINT_STATUS_FOUND: Hint['status'] = 40;

  constructor() {
    this.items = computed(() => {
      const victoryLocationYamlKey = this.#gameStore.victoryLocationYamlKey();
      const lactoseIntolerant = this.#gameStore.lactoseIntolerant();
      return [{
        index: 0,
        id: 'rats',
        collected: computed(() => this.ratCount() > 0),
        name: 'Rats',
        flavorText: null,
        offsetX: computed(() => 0),
        offsetY: BAKED_DEFINITIONS_FULL.progressionItemsByYamlKey.size * 65,
      }, ...PROGRESSION_ITEMS_BY_VICTORY_LOCATION[victoryLocationYamlKey].map((itemYamlKey, index) => {
        const item = BAKED_DEFINITIONS_FULL.progressionItemsByYamlKey.get(itemYamlKey) ?? -1;
        const collected = computed(() => this.#performanceInsensitiveAnimatableState.receivedItemCountLookup()[item] > 0);
        return {
          index: index + 1,
          id: item,
          name: lactoseIntolerant
            ? BAKED_DEFINITIONS_FULL.allItems[item].lactoseIntolerantName
            : BAKED_DEFINITIONS_FULL.allItems[item].lactoseName,
          flavorText: BAKED_DEFINITIONS_FULL.allItems[item].flavorText,
          collected,
          offsetX: computed(() => collected() ? 0 : 65),
          offsetY: index * 65,
        };
      })];
    });
  }

  protected setTooltipOrigin(index: number, item: number | 'rats', props: TooltipOriginProps | null, fromDirective: boolean) {
    this.#tooltipOrigin.update((prev) => {
      if (prev !== null && !fromDirective) {
        prev.notifyDetached();
      }
      return props === null
        ? null
        : { index, item, ...props };
    });
  }

  protected onClickItem(item: ItemModel, clicked: HTMLElement) {
    const game = this.#gameStore.game();
    if (!game) {
      return;
    }

    const dialogConfig = {
      data: { itemName: item.name },
      positionStrategy: createFlexibleConnectedPositionStrategy(this.#injector, clicked)
        .withPositions([{
          originX: 'start',
          originY: 'bottom',
          overlayX: 'start',
          overlayY: 'top',
        }]),
    } as const;
    this.#dialog.open<boolean>(RequestHint, dialogConfig).closed
      .subscribe((confirm) => {
        if (confirm) {
          void game.client.messages.say(`!hint ${item.name}`);
          clicked.blur();
        }
      });
  }

  protected isSelf(player: Player) {
    const game = this.#gameStore.game();
    if (game === null) {
      return false;
    }

    const { team, slot } = game.client.players.self;
    return player.team === team && player.slot === slot;
  }

  protected statusText(hint: Hint) {
    // https://github.com/ArchipelagoMW/Archipelago/blob/0.6.5/kvui.py#L1195-L1201
    switch (hint.status) {
      case this.HINT_STATUS_FOUND: return 'Found';
      case this.HINT_STATUS_UNSPECIFIED: return 'Unspecified';
      case this.HINT_STATUS_NO_PRIORITY: return 'No Priority';
      case this.HINT_STATUS_AVOID: return 'Avoid';
      case this.HINT_STATUS_PRIORITY: return 'Priority';
      default: return null;
    }
  }
}

interface ItemModel {
  index: number;
  id: number | 'rats';
  name: string;
  flavorText: string | null;
  collected: Signal<boolean>;
  offsetX: Signal<number>;
  offsetY: number;
}

interface CurrentTooltipOriginProps {
  uid: symbol;
  index: number;
  item: number | 'rats';
  element: HTMLElement;
  notifyDetached: () => void;
}
