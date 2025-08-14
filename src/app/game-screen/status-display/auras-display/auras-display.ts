import {
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';

import { resizeText } from '../../../util';

@Component({
  selector: 'app-auras-display',
  imports: [],
  template: `
    <details #outer class="outer" open>
      <summary #ratCountElement class="rat-count">RATS: {{ ratCount() }}</summary>
      <div class="bulk-auras">
        <span class="bar-label">Food</span>
        <div class="bar-track">
          <div class="bar-center-notch"></div>
          <div class="bar-fill" [style.width.%]="foodFillPercentage()" [class.negative]="food() < 0"></div>
        </div>
        <span class="bar-value">{{ food() }}</span>
        <span class="bar-label">Energy</span>
        <div class="bar-track">
          <div class="bar-center-notch"></div>
          <div class="bar-fill" [style.width.%]="energyFillPercentage()" [class.negative]="energy() < 0"></div>
        </div>
        <span class="bar-value">{{ energy() }}</span>
        <span class="bar-label">Luck</span>
        <div class="bar-track">
          <div class="bar-center-notch"></div>
          <div class="bar-fill" [style.width.%]="luckFillPercentage()" [class.negative]="luck() < 0"></div>
        </div>
        <span class="bar-value">{{ luck() }}</span>
      </div>
      <div class="normative-auras">
        <div class="normative-aura">
          <span class="normative" [class.lit]="distraction() > 0">Distract</span>
          <span class="normative normative-extra" [class.lit]="distraction() > 1">(x2)</span>
          <span class="normative normative-extra" [class.lit]="distraction() > 2">(x3)</span>
        </div>
        <div class="normative-aura normative-right">
          <span class="normative" [class.lit]="startled() > 0">Startle</span>
          <span class="normative normative-extra" [class.lit]="startled() > 1">(x2)</span>
          <span class="normative normative-extra" [class.lit]="startled() > 2">(x3)</span>
        </div>
        <div class="normative-aura">
          <span class="normative" [class.lit]="smart()">Smart</span>
        </div>
        <div class="normative-aura normative-right">
          <span class="normative" [class.lit]="conspiratorial()">Conspiratorial</span>
        </div>
        <div class="normative-aura">
          <span class="normative" [class.lit]="stylish() > 0">Style</span>
          <span class="normative normative-extra" [class.lit]="stylish() > 1">(x2)</span>
          <span class="normative normative-extra" [class.lit]="stylish() > 2">(x3+)</span>
        </div>
        <div class="normative-aura normative-right">
          <span class="normative" [class.lit]="confidence()">Confidence</span>
        </div>
      </div>
    </details>
  `,
  styles: `
    .outer {
      margin-left: 5px;
      margin-right: 5px;
      container-type: inline-size;
    }

    .rat-count {
      text-align: start;
      font-size: 30px;
      text-wrap: nowrap;
    }

    .bulk-auras {
      display: grid;
      grid-template-columns: auto 1fr auto;
      grid-template-rows: auto auto auto;
      gap: 5px 0;
      grid-auto-flow: row;
      grid-template-areas:
        "bar-labels bars bar-values"
        "bar-labels bars bar-values"
        "bar-labels bars bar-values";
    }

    .bar-track {
      position: relative;
      flex: 1;
      height: 20px;
      background-color: #333333;
      border: 1px solid #666666;
      border-radius: 4px;
      overflow: hidden;
      margin-left: 5px;
      margin-right: 5px;
    }

    .bar-center-notch {
      position: absolute;
      left: 50%;
      top: 0;
      width: 2px;
      height: 100%;
      background-color: #ffffff;
      transform: translateX(-50%);
      z-index: 2;
    }

    .bar-fill {
      position: absolute;
      top: 0;
      height: 100%;
      transition: width 0.3s ease;
      z-index: 1;
    }

    .bar-fill:not(.negative) {
      left: 50%;
      background-color: #4CAF50;
    }

    .bar-fill.negative {
      right: 50%;
      background-color: #f44336;
    }

    .bar-value {
      text-align: end;
    }

    .normative-auras {
      margin-top: 20px;
    }

    @container (width > 460px) {
      .normative-auras {
        display: grid;
        grid-auto-columns: 1fr;
        grid-template-columns: 1fr 1fr;
        grid-template-rows: 1fr 1fr 1fr;
        gap: 0;
      }

      .normative-right {
        text-align: end;
      }

      .normative-aura {
        display: inline-block;
      }
    }

    .normative-aura {
      white-space: nowrap;
    }

    .normative {
      color: #606060;
      margin-right: 5px;

      &.lit {
        color: yellow;
      }
    }

    .normative-extra {
      font-size: x-small;
    }
  `,
})
export class AurasDisplay {
  readonly outerElement = viewChild.required<ElementRef<HTMLElement>>('outer');
  readonly ratCountElement = viewChild.required<ElementRef<HTMLElement>>('ratCountElement');

  readonly ratCount = signal(21);

  readonly food = signal(-2);
  readonly energy = signal(5);
  readonly luck = signal(0);

  readonly distraction = signal(2);
  readonly startled = signal(1);
  readonly smart = signal(false);
  readonly conspiratorial = signal(true);
  readonly stylish = signal(1);
  readonly confidence = signal(false);

  readonly foodFillPercentage = computed(() => {
    const value = Math.max(-20, Math.min(20, this.food()));
    return Math.abs(value / 20) * 50;
  });

  readonly energyFillPercentage = computed(() => {
    const value = Math.max(-20, Math.min(20, this.energy()));
    return Math.abs(value / 20) * 50;
  });

  readonly luckFillPercentage = computed(() => {
    const value = Math.max(-5, Math.min(5, this.luck()));
    return Math.abs(value / 5) * 50;
  });

  constructor() {
    const destroy = inject(DestroyRef);
    const injector = inject(Injector);
    resizeText({ outer: this.outerElement, inner: this.ratCountElement, max: 30, destroy, injector });
  }
}
