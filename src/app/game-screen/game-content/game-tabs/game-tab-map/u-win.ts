import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { NgOptimizedImage } from '@angular/common';
import { Component, inject, type Signal } from '@angular/core';
import { type ElementSize } from '../../../../utils/element-size';

@Component({
  selector: 'app-u-win',
  imports: [
    NgOptimizedImage,
  ],
  template: `
    <div class="outer" [style.height.px]="size().clientHeight * .6" [style.width.px]="size().clientWidth * .6">
      <div class="uwin-and-flag">
        <span>u win.</span><img alt="moon_comma_the" ngSrc="/assets/images/locations/moon_comma_the.webp" width="64" height="64">
      </div>
      <button (click)="dialogRef.close()">Back to Map</button>
    </div>
  `,
  styles: `
    @use '../../../../../theme';

    .outer {
      display: flex;
      flex-direction: column;
      gap: 40px;
      background-color: theme.$region-color;
      align-items: center;
      justify-content: center;
    }

    .uwin-and-flag {
      font-size: 40pt;
      pointer-events: none;
      user-select: none;
    }
  `,
})
export class UWin {
  readonly size = inject<Signal<ElementSize>>(DIALOG_DATA);
  protected readonly dialogRef = inject(DialogRef);
}
