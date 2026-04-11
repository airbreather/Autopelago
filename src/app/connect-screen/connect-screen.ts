import { Dialog } from '@angular/cdk/dialog';
import { createGlobalPositionStrategy } from '@angular/cdk/overlay';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  type ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';

import { disabled, form, FormField, max, min, required } from '@angular/forms/signals';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { TinyColor } from '@ctrl/tinycolor';
import versionInfo from '../../version-info.json';
import { applyPixelColors, getPixelTones } from '../utils/color-helpers';
import { elementSizeSignal } from '../utils/element-size';
import {
  connectStateFromStorageModifiedByQueryParams,
  queryParamsFromConnectScreenState,
  saveToStorage,
} from './connect-screen-state';
import { Personalize, type PersonalizeData } from './personalize';

@Component({
  selector: 'app-connect-screen',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormField,
  ],
  template: `
    <form #allInputs class="root" (submit)="onConnect($event)">
      <div class="inputs">
        <label for="slot">Slot:</label>
        <div class="slot-and-personalize">
          <input id="slot"
                 type="text"
                 [formField]="form.slot"/>
          <button id="personalize"
                  type="button"
                  (click)="$event.preventDefault(); openPersonalizeDialog()">
            <canvas #personalizeButtonCanvas width="64" height="64"
                    [style.height.px]="textBoxHeight()">
            </canvas>
          </button>
        </div>
        <label for="host">Host:</label>
        <input #hostTextBox
               id="host"
               type="text"
               [formField]="form.host"/>
        <label for="port">Port:</label>
        <input id="port"
               type="number"
               [formField]="form.port"/>
        <label for="password">Password:</label>
        <input id="password"
               type="password"
               [formField]="form.password"/>
        <fieldset class="inputs" [style.grid-column]="'span 2'">
          <legend>Time Between Steps (sec.)</legend>
          <label for="minTimeSeconds">Minimum:</label>
          <input id="minTimeSeconds"
                 type="number"
                 step="any"
                 [formField]="form.minTimeSeconds"/>
          <label for="maxTimeSeconds">Maximum:</label>
          <input id="maxTimeSeconds"
                 type="number"
                 step="any"
                 [formField]="form.maxTimeSeconds"/>
        </fieldset>
      </div>
      <div class="inputs">
        <input id="enableTileAnimations"
               type="checkbox"
               [formField]="form.enableTileAnimations"/>
        <label for="enableTileAnimations">Enable tile animations</label>

        <input id="enableRatAnimations"
               type="checkbox"
               [formField]="form.enableRatAnimations"/>
        <label for="enableRatAnimations">Enable rat animations</label>

        <input id="sendChatMessages"
               type="checkbox"
               [formField]="form.sendChatMessages"/>
        <label for="sendChatMessages">Send chat messages...</label>
      </div>
      <div class="inputs indent-chat-message-details">
        <input id="whenDeathIsImminent"
               type="checkbox"
               [formField]="form.whenDeathIsImminent"/>
        <label for="whenDeathIsImminent">when death is imminent</label>

        <input id="whenTargetChanges"
               type="checkbox"
               [formField]="form.whenTargetChanges"/>
        <label for="whenTargetChanges">when target changes</label>

        <input id="whenBecomingBlocked"
               type="checkbox"
               [formField]="form.whenBecomingBlocked"/>
        <label for="whenBecomingBlocked">when becoming blocked</label>

        <input id="whenStillBlocked"
               type="checkbox"
               [formField]="form.whenStillBlocked"/>
        <label for="whenStillBlocked">when STILL blocked</label>

        <div class="indent-report-interval">
          <label for="whenStillBlockedIntervalMinutes">every </label>
          <input id="whenStillBlockedIntervalMinutes"
                 class="short-number"
                 type="number"
                 [formField]="form.whenStillBlockedIntervalMinutes"/>
          <label for="whenStillBlockedIntervalMinutes"> minutes</label>
        </div>

        <input id="whenBecomingUnblocked"
               type="checkbox"
               [formField]="form.whenBecomingUnblocked"/>
        <label for="whenBecomingUnblocked">when becoming unblocked</label>

        <input id="forOneTimeEvents"
               type="checkbox"
               [formField]="form.forOneTimeEvents"/>
        <label for="forOneTimeEvents">for one-time events</label>
      </div>
      <input class="submit-button"
             type="submit"
             [disabled]="connecting() || !form().valid()"
             [value]="connecting() ? 'Connecting...' : 'Connect'"/>
    </form>
  `,
  styles: `
    .root {
      display: flex;
      flex-direction: column;
      margin: 5px;

      > :not(:first-child) {
        margin-top: 5px;
      }
    }

    .inputs {
      display: grid;
      gap: calc(5rem / 16);
      grid-template-columns: max-content 1fr;

      label {
        align-self: center;
        justify-self: start;
      }
    }

    .slot-and-personalize {
      display: grid;
      gap: calc(5rem / 16);
      grid-template-columns: 1fr max-content;
      #personalize {
        width: 100%;
        height: 100%;
        padding: 0;
      }
    }

    .indent-chat-message-details {
      padding-left: 20px;
    }

    .indent-report-interval {
      grid-column: span 2;
      padding-left: 40px;
    }

    .short-number {
      width: calc(4ch + 60px);
    }
  `,
})
export class ConnectScreen {
  readonly #router = inject(Router);
  readonly #route = inject(ActivatedRoute);
  readonly #dialog = inject(Dialog);
  readonly #connecting = signal(false);
  protected readonly connecting = this.#connecting.asReadonly();

  protected readonly hostTextBox = viewChild.required<ElementRef<HTMLInputElement>>('hostTextBox');
  readonly #hostTextBoxSize = elementSizeSignal(this.hostTextBox);
  protected readonly textBoxHeight = computed(() => this.#hostTextBoxSize().clientHeight);

  protected readonly personalizeButtonCanvas = viewChild.required<ElementRef<HTMLCanvasElement>>('personalizeButtonCanvas');
  readonly #formModel = signal(connectStateFromStorageModifiedByQueryParams(this.#route.snapshot.queryParamMap));

  protected readonly form = form(this.#formModel, (schemaPath) => {
    /* eslint-disable @typescript-eslint/unbound-method */
    required(schemaPath.slot);
    required(schemaPath.host);
    required(schemaPath.port);
    min(schemaPath.port, 1);
    max(schemaPath.port, 65535);

    required(schemaPath.minTimeSeconds);
    required(schemaPath.maxTimeSeconds);
    min(schemaPath.minTimeSeconds, 0.01);
    max(schemaPath.minTimeSeconds, ({ valueOf }) => Math.max(0.01, valueOf(schemaPath.maxTimeSeconds)));
    min(schemaPath.maxTimeSeconds, ({ valueOf }) => Math.max(0.01, valueOf(schemaPath.minTimeSeconds)));

    disabled(schemaPath.whenDeathIsImminent, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    disabled(schemaPath.whenTargetChanges, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    disabled(schemaPath.whenBecomingBlocked, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    disabled(schemaPath.whenStillBlocked, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages) || !valueOf(schemaPath.whenBecomingBlocked));
    disabled(schemaPath.whenStillBlockedIntervalMinutes, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages) || !valueOf(schemaPath.whenBecomingBlocked) || !valueOf(schemaPath.whenStillBlocked));
    min(schemaPath.whenStillBlockedIntervalMinutes, 15);
    disabled(schemaPath.whenBecomingUnblocked, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    disabled(schemaPath.forOneTimeEvents, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    /* eslint-enable @typescript-eslint/unbound-method */
  });

  constructor() {
    // this is semi-repeated in player-name-and-navigation.ts, but we have a completely different
    // thing that "player name" could mean, so copy-paste isn't bad.
    const title = inject(Title);
    effect(() => {
      const slot = this.form.slot().value();
      const slotPart = slot ? `${slot} | ` : '';
      title.setTitle(`${slotPart}Autopelago ${versionInfo.version} | A Game So Easy, It Plays Itself!`);
    });

    effect(() => {
      const portState = this.form.port();
      const hostState = this.form.host();
      if (portState.valid() && portState.dirty()) {
        const port = portState.value();
        hostState.value.update(host => Number.isInteger(port)
          ? host.replace(/(?<=:)\d+$/, port.toString())
          : host.replace(/:\d+$/, ''));
      }
      else if (hostState.valid() && hostState.dirty()) {
        const host = hostState.value();
        const m = /(?<=:)\d+$/.exec(host);
        if (m) {
          portState.value.set(Number(m[0]));
        }
      }

      portState.reset();
      hostState.reset();
    });

    let firstLoad = true;
    effect(() => {
      const state = this.form().value();
      saveToStorage(state);
      if (firstLoad) {
        firstLoad = false;
      }
      else {
        void this.#router.navigate([], {
          relativeTo: this.#route,
          queryParams: queryParamsFromConnectScreenState(state),
          queryParamsHandling: 'replace',
          replaceUrl: true,
        });
      }
    });

    const initialImageDraw = effect(() => {
      const canvas = this.personalizeButtonCanvas().nativeElement.getContext('2d');
      if (canvas === null) {
        return;
      }

      const tones = getPixelTones(this.form.playerIcon().value());
      const data = canvas.createImageData(64, 64);
      applyPixelColors(new TinyColor(this.form.playerColor().value()), tones, data.data);
      canvas.putImageData(data, 0, 0);
      initialImageDraw.destroy();
    });
  }

  async onConnect(event: SubmitEvent) {
    event.preventDefault();
    try {
      this.#connecting.set(true);
      await this.#router.navigate(['./game'], {
        queryParams: queryParamsFromConnectScreenState(this.form().value()),
      });
    }
    catch (err: unknown) {
      this.#connecting.set(false);
      throw err;
    }
  }

  readonly #injector = inject(Injector);
  protected openPersonalizeDialog() {
    this.#dialog.open(Personalize, {
      data: {
        playerIcon: this.form.playerIcon().value,
        playerColor: this.form.playerColor().value,
        canvas: this.personalizeButtonCanvas().nativeElement.getContext('2d'),
      } as PersonalizeData,
      positionStrategy: createGlobalPositionStrategy(this.#injector)
        .right()
        .top(),
    });
  }
}
