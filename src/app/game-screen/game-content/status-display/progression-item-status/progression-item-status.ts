import { Dialog } from '@angular/cdk/dialog';
import { CdkConnectedOverlay, CdkOverlayOrigin, createFlexibleConnectedPositionStrategy } from '@angular/cdk/overlay';
import { ChangeDetectionStrategy, Component, computed, inject, Injector, signal, type Signal } from '@angular/core';
import { PROGRESSION_ITEMS_BY_VICTORY_LOCATION } from '../../../../data/items';
import { BAKED_DEFINITIONS_FULL } from '../../../../data/resolved-definitions';

import { GameStore } from '../../../../store/autopelago-store';
import { RequestHint } from './request-hint';

const TOOLTIP_DELAY = 400;

@Component({
  selector: 'app-progression-item-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CdkConnectedOverlay,
    CdkOverlayOrigin,
  ],
  template: `
    <div #outer class="outer">
      @for (item of items(); track item.name) {
        <div #thisContainer class="item-container" [class.collected]="item.collected()" cdkOverlayOrigin
             [tabindex]="$index + 500" (click)="onClickItem(item, thisContainer)"
             (keyup.enter)="onClickItem(item, thisContainer)" (keyup.space)="onClickItem(item, thisContainer)"
             (focus)="onFocusTooltip($index, thisContainer)" (mouseenter)="onFocusTooltip($index, thisContainer)"
             (blur)="onBlurTooltip()" (mouseleave)="onBlurTooltip()">
          <!--suppress AngularNgOptimizedImage -->
          <img class="item"
               src="/assets/images/items.webp"
               [alt]="item.name"
               [style.object-position]="-item.offsetX() + 'px ' + -item.offsetY + 'px'">
        </div>
      }
    </div>
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="tooltipTarget()?.[1] ?? outer"
      [cdkConnectedOverlayOpen]="tooltipTarget() !== null"
      [cdkConnectedOverlayUsePopover]="'inline'"
      (detach)="detachTooltip()">
      @let item = items()[tooltipTarget()![0]];
      <div class="box tooltip">
        <h1 class="header">{{item.name}}</h1>
        <div class="flavor-text" [hidden]="!item.flavorText">“{{item.flavorText}}”</div>
      </div>
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

      border: 2px solid black;
      border-radius: 8px;

      &.collected {
        border: 2px solid #FFCE00;
      }
    }

    .item {
      object-fit: none;
      width: 64px;
      height: 64px;
    }

    .box {
      padding: 4px;
      border: 2px solid black;
    }

    .tooltip {
      display: grid;
      grid-template-columns: max-content;
      grid-auto-rows: auto;
      gap: 10px;
      background-color: theme.$region-color;

      .header {
        margin: 0;
        font-size: 14pt;
        white-space: nowrap;
      }

      .flavor-text {
        font-size: 8pt;
        max-width: 430px;
      }
    }
  `,
})
export class ProgressionItemStatus {
  readonly #injector = inject(Injector);
  readonly #dialog = inject(Dialog);
  readonly #gameStore = inject(GameStore);
  protected readonly items: Signal<readonly ItemModel[]>;
  readonly #tooltipTarget = signal<[number, HTMLElement] | null>(null);
  protected readonly tooltipTarget = this.#tooltipTarget.asReadonly();

  constructor() {
    this.items = computed(() => {
      const victoryLocationYamlKey = this.#gameStore.victoryLocationYamlKey();
      const lactoseIntolerant = this.#gameStore.lactoseIntolerant();
      return PROGRESSION_ITEMS_BY_VICTORY_LOCATION[victoryLocationYamlKey].map((itemYamlKey, index) => {
        const item = BAKED_DEFINITIONS_FULL.progressionItemsByYamlKey.get(itemYamlKey) ?? -1;
        const collected = computed(() => this.#gameStore.receivedItemCountLookup()[item] > 0);
        return {
          name: lactoseIntolerant
            ? BAKED_DEFINITIONS_FULL.allItems[item].lactoseIntolerantName
            : BAKED_DEFINITIONS_FULL.allItems[item].lactoseName,
          flavorText: BAKED_DEFINITIONS_FULL.allItems[item].flavorText,
          collected,
          offsetX: computed(() => collected() ? 0 : 65),
          offsetY: index * 65,
        };
      });
    });
  }

  #prevFocusTimeout = NaN;
  #prevBlurTimeout = NaN;
  onFocusTooltip(item: number, element: HTMLDivElement) {
    if (!Number.isNaN(this.#prevBlurTimeout)) {
      clearTimeout(this.#prevBlurTimeout);
      this.#prevBlurTimeout = NaN;
      this.#tooltipTarget.set([item, element]);
      return;
    }

    if (!Number.isNaN(this.#prevFocusTimeout)) {
      clearTimeout(this.#prevFocusTimeout);
      this.#prevFocusTimeout = NaN;
    }
    this.#prevFocusTimeout = setTimeout(() => {
      this.#tooltipTarget.set([item, element]);
      this.#prevFocusTimeout = NaN;
    }, TOOLTIP_DELAY);
  }

  onBlurTooltip() {
    if (!Number.isNaN(this.#prevFocusTimeout)) {
      clearTimeout(this.#prevFocusTimeout);
      this.#prevFocusTimeout = NaN;
      this.#tooltipTarget.set(null);
      return;
    }

    if (!Number.isNaN(this.#prevBlurTimeout)) {
      clearTimeout(this.#prevBlurTimeout);
      this.#prevBlurTimeout = NaN;
    }
    this.#prevBlurTimeout = setTimeout(() => {
      this.#tooltipTarget.set(null);
      this.#prevBlurTimeout = NaN;
    }, TOOLTIP_DELAY);
  }

  protected detachTooltip() {
    if (!Number.isNaN(this.#prevFocusTimeout)) {
      clearTimeout(this.#prevFocusTimeout);
      this.#prevFocusTimeout = NaN;
    }

    if (!Number.isNaN(this.#prevBlurTimeout)) {
      clearTimeout(this.#prevBlurTimeout);
      this.#prevBlurTimeout = NaN;
    }

    this.#tooltipTarget.set(null);
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
        }
      });
  }
}

interface ItemModel {
  name: string;
  flavorText: string | null;
  collected: Signal<boolean>;
  offsetX: Signal<number>;
  offsetY: number;
}
