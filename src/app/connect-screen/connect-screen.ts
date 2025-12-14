import { Dialog } from '@angular/cdk/dialog';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  type ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { disabled, Field, form, max, min, required } from '@angular/forms/signals';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import versionInfo from '../../version-info.json';
import { elementSizeSignal } from '../utils/element-size';
import { trySetBooleanProp, trySetNumberProp, trySetStringProp } from '../utils/hardened-state-propagation';
import {
  CONNECT_SCREEN_STATE_DEFAULTS,
  type ConnectScreenState,
  queryParamsFromConnectScreenState,
} from './connect-screen-state';
import { Personalize } from './personalize';

// Local storage key
const STORAGE_KEY = 'autopelago-connect-screen-state';

@Component({
  selector: 'app-connect-screen',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    Field,
  ],
  template: `
    <form #allInputs class="root" (submit)="onConnect($event)">
      <div class="inputs">
        <label for="slot">Slot:</label>
        <div #slotAndPersonalize class="slot-and-personalize">
          <input #slot
                 id="slot"
                 type="text"
                 [field]="form.slot"/>
          <button id="personalize"
                 (click)="$event.preventDefault(); openPersonalizeDialog()">
            <img src="/assets/images/players/pack_rat.webp" alt="Personalize">
          </button>
        </div>
        <label for="host">Host:</label>
        <input id="host"
               type="text"
               [field]="form.host"/>
        <label for="port">Port:</label>
        <input id="port"
               type="number"
               [field]="form.port"/>
        <label for="password">Password:</label>
        <input id="password"
               type="password"
               [field]="form.password"/>
        <fieldset class="inputs" [style.grid-column]="'span 2'">
          <legend>Time Between Steps (sec.)</legend>
          <label for="minTimeSeconds">Minimum:</label>
          <input id="minTimeSeconds"
                 type="number"
                 step="any"
                 [field]="form.minTimeSeconds"/>
          <label for="maxTimeSeconds">Maximum:</label>
          <input id="maxTimeSeconds"
                 type="number"
                 step="any"
                 [field]="form.maxTimeSeconds"/>
        </fieldset>
      </div>
      <div class="inputs">
        <input id="enableTileAnimations"
               type="checkbox"
               [field]="form.enableTileAnimations"/>
        <label for="enableTileAnimations">Enable tile animations</label>

        <input id="enableRatAnimations"
               type="checkbox"
               [field]="form.enableRatAnimations"/>
        <label for="enableRatAnimations">Enable rat animations</label>

        <input id="sendChatMessages"
               type="checkbox"
               [field]="form.sendChatMessages"/>
        <label for="sendChatMessages">Send chat messages...</label>
      </div>
      <div class="inputs indent-chat-message-details">
        <input id="whenTargetChanges"
               type="checkbox"
               [field]="form.whenTargetChanges"/>
        <label for="whenTargetChanges">when target changes</label>

        <input id="whenBecomingBlocked"
               type="checkbox"
               [field]="form.whenBecomingBlocked"/>
        <label for="whenBecomingBlocked">when becoming blocked</label>

        <input id="whenStillBlocked"
               type="checkbox"
               [field]="form.whenStillBlocked"/>
        <label for="whenStillBlocked">when STILL blocked</label>

        <div class="indent-report-interval">
          <label for="whenStillBlockedIntervalMinutes">every </label>
          <input id="whenStillBlockedIntervalMinutes"
                 class="short-number"
                 type="number"
                 [field]="form.whenStillBlockedIntervalMinutes"/>
          <label for="whenStillBlockedIntervalMinutes"> minutes</label>
        </div>

        <input id="whenBecomingUnblocked"
               type="checkbox"
               [field]="form.whenBecomingUnblocked"/>
        <label for="whenBecomingUnblocked">when becoming unblocked</label>

        <input id="forOneTimeEvents"
               type="checkbox"
               [field]="form.forOneTimeEvents"/>
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
      #slot {
        flex: 1;
      }
      #personalize {
        width: var(--ap-slot-height, 0);
        height: var(--ap-slot-height, 0);
        padding: 0;
        img {
          width: 100%;
          height: 100%;
          object-fit: contain;
        }
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
  readonly #dialog = inject(Dialog);
  readonly #connecting = signal(false);
  protected readonly connecting = this.#connecting.asReadonly();
  protected readonly slotElement = viewChild.required<ElementRef<HTMLInputElement>>('slot');
  protected readonly slotAndPersonalizeElement = viewChild.required<ElementRef<HTMLDivElement>>('slotAndPersonalize');
  readonly #formModel = signal(CONNECT_SCREEN_STATE_DEFAULTS);
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

    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved) {
      let parsed: unknown = null;
      try {
        parsed = JSON.parse(saved) as unknown;
      }
      catch {
        console.warn('Failed to parse saved connect screen state:', saved);
      }

      if (typeof parsed !== 'object' || parsed === null) {
        return;
      }

      const model: Partial<ConnectScreenState> = { };
      trySetStringProp(parsed, 'slot', model);
      trySetStringProp(parsed, 'host', model);
      trySetNumberProp(parsed, 'port', model);
      trySetStringProp(parsed, 'password', model);
      trySetNumberProp(parsed, 'minTimeSeconds', model);
      trySetNumberProp(parsed, 'maxTimeSeconds', model);
      trySetBooleanProp(parsed, 'enableTileAnimations', model);
      trySetBooleanProp(parsed, 'enableRatAnimations', model);
      trySetBooleanProp(parsed, 'sendChatMessages', model);
      trySetBooleanProp(parsed, 'whenTargetChanges', model);
      trySetBooleanProp(parsed, 'whenBecomingBlocked', model);
      trySetBooleanProp(parsed, 'whenStillBlocked', model);
      trySetNumberProp(parsed, 'whenStillBlockedIntervalMinutes', model);
      trySetBooleanProp(parsed, 'whenBecomingUnblocked', model);
      trySetBooleanProp(parsed, 'forOneTimeEvents', model);
      this.#formModel.set({
        ...CONNECT_SCREEN_STATE_DEFAULTS,
        ...model,
      });
    }

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

    effect(() => {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.form().value()));
    });

    const slotElementSizeSignal = elementSizeSignal(this.slotElement);
    const slotElementHeightSignal = computed(() => slotElementSizeSignal().clientHeight);
    effect(() => {
      this.slotAndPersonalizeElement().nativeElement.style.setProperty('--ap-slot-height', `${slotElementHeightSignal().toString()}px`);
    });

    this.openPersonalizeDialog();
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

  protected openPersonalizeDialog() {
    this.#dialog.open(Personalize);
  }
}
