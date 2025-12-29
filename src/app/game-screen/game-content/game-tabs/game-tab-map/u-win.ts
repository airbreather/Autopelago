import { DialogRef } from '@angular/cdk/dialog';
import { NgOptimizedImage } from '@angular/common';
import { Component, inject } from '@angular/core';

@Component({
  selector: 'app-u-win',
  imports: [
    NgOptimizedImage,
  ],
  template: `
    <div class="outer">
      <div class="uwin-and-flag">
        <span>u win.</span><img alt="moon_comma_the" ngSrc="/assets/images/moon_comma_the.webp" width="64" height="64">
      </div>
      <button (click)="dialogRef.close()">Back to Map</button>
    </div>
  `,
  styles: `
    @use '../../../../../theme';

    .outer {
      width: 100%;
      height: 100%;
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
  protected readonly dialogRef = inject(DialogRef);
}
