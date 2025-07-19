import {
  Component,
  signal,
  computed,
  ElementRef,
  inject,
  DestroyRef,
  Injector,
  viewChild, AfterViewInit,
} from '@angular/core';

import { resizeText } from '../../../util';

@Component({
  selector: 'app-auras-display',
  imports: [],
  template: `
    <details #outer class="outer" open>
      <summary #ratCountElement class="rat-count">RATS: {{ ratCount() }}</summary>
      <div class="bars">
        <div class="bar-container">
          <span class="bar-label">Food</span>
          <div class="bar-track">
            <div class="bar-center-notch"></div>
            <div class="bar-fill" [style.width.%]="foodFillPercentage()" [class.negative]="food() < 0"></div>
          </div>
          <span class="bar-value">{{ food() }}</span>
        </div>
        <div class="bar-container">
          <span class="bar-label">Energy</span>
          <div class="bar-track">
            <div class="bar-center-notch"></div>
            <div class="bar-fill" [style.width.%]="energyFillPercentage()" [class.negative]="energy() < 0"></div>
          </div>
          <span class="bar-value">{{ energy() }}</span>
        </div>
        <div class="bar-container">
          <span class="bar-label">Luck</span>
          <div class="bar-track">
            <div class="bar-center-notch"></div>
            <div class="bar-fill" [style.width.%]="luckFillPercentage()" [class.negative]="luck() < 0"></div>
          </div>
          <span class="bar-value">{{ luck() }}</span>
        </div>
      </div>
    </details>
  `,
  styles: `
    .outer {
      margin-left: 5px;
      margin-right: 5px;
    }

    .rat-count {
      text-align: start;
      font-size: 50px;
      text-wrap: nowrap;
    }

    .bars {
      display: flex;
      flex-direction: column;
      gap: 8px;
      margin-top: 16px;
    }

    .bar-container {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .bar-label {
      width: 80px;
      font-weight: bold;
      font-size: 14px;
      text-align: right;
      flex-shrink: 0;
    }

    .bar-track {
      position: relative;
      flex: 1;
      height: 20px;
      background-color: #333;
      border: 1px solid #666;
      border-radius: 4px;
      overflow: hidden;
    }

    .bar-center-notch {
      position: absolute;
      left: 50%;
      top: 0;
      width: 2px;
      height: 100%;
      background-color: #fff;
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
  `,
})
export class AurasDisplay implements AfterViewInit {
  readonly #destroy = inject(DestroyRef);
  readonly #injector = inject(Injector);

  readonly outerElement = viewChild.required<ElementRef<HTMLElement>>('outer');
  readonly ratCountElement = viewChild.required<ElementRef<HTMLElement>>('ratCountElement');

  readonly ratCount = signal(21);

  readonly food = signal(-2);
  readonly energy = signal(5);
  readonly luck = signal(0);

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

  ngAfterViewInit(): void {
    resizeText({ outer: this.outerElement, inner: this.ratCountElement, max: 50, destroy: this.#destroy, injector: this.#injector });
  }
}
