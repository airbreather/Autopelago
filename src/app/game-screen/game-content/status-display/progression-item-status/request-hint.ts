import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject } from '@angular/core';

export interface RequestHintData {
  itemName: string;
}

@Component({
  selector: 'app-request-hint',
  imports: [],
  template: `
    <div class="outer">
      <div class="confirm">Request hint for<br/><span class="item-name">{{itemName}}</span>?</div>
      <div class="ok-cancel">
        <button class="ok-cancel-button" (click)="dialogRef.close(true)">OK</button>
        <div class="spacer"></div>
        <button class="ok-cancel-button" (click)="dialogRef.close(false)">Cancel</button>
      </div>
    </div>
  `,
  styles: `
    @use '../../../../../theme';

    .outer {
      max-width: 400px;
      display: flex;
      flex-direction: column;
      gap: 10px;
      font-size: 14px;
      background-color: theme.$region-color;
      align-items: center;
      justify-content: center;
    }

    .confirm {
      pointer-events: none;
      user-select: none;
    }

    .item-name {
      color: theme.$error-text;
    }

    .ok-cancel {
      display: flex;
      width: 100%;
      flex-direction: row;
    }

    .ok-cancel-button {
      width: min-content;
      margin: 5px auto;
    }
  `,
})
export class RequestHint {
  protected readonly dialogRef = inject(DialogRef);
  readonly #dialogData = inject<RequestHintData>(DIALOG_DATA);
  protected readonly itemName = this.#dialogData.itemName;
}
