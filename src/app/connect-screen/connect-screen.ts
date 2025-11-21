import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';

import { disabled, Field, form, max, min, required } from '@angular/forms/signals';
import { Router } from '@angular/router';
import {
  CONNECT_SCREEN_STATE_DEFAULTS,
  type ConnectScreenState,
  queryParamsFromConnectScreenState,
} from './connect-screen-state';

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
        <input id="slot"
               type="text"
               [field]="form.slot"/>
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
                 [field]="form.minTimeSeconds"/>
          <label for="maxTimeSeconds">Maximum:</label>
          <input id="maxTimeSeconds"
                 type="number"
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
      <div class="inputs" [style.padding-left]="'20px'">
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
  `,
})
export class ConnectScreen {
  readonly #router = inject(Router);
  readonly #connecting = signal(false);
  readonly connecting = this.#connecting.asReadonly();
  readonly model = signal(CONNECT_SCREEN_STATE_DEFAULTS);
  readonly form = form(this.model, (schemaPath) => {
    /* eslint-disable @typescript-eslint/unbound-method */
    required(schemaPath.slot);
    required(schemaPath.host);
    required(schemaPath.port);
    min(schemaPath.port, 1);
    max(schemaPath.port, 65535);

    required(schemaPath.minTimeSeconds);
    required(schemaPath.maxTimeSeconds);
    max(schemaPath.minTimeSeconds, ({ valueOf }) => Math.max(0.1, valueOf(schemaPath.maxTimeSeconds)));
    min(schemaPath.maxTimeSeconds, ({ valueOf }) => Math.max(0.1, valueOf(schemaPath.minTimeSeconds)));

    disabled(schemaPath.whenTargetChanges, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    disabled(schemaPath.whenBecomingBlocked, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    disabled(schemaPath.whenStillBlocked, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages) || !valueOf(schemaPath.whenBecomingBlocked));
    disabled(schemaPath.whenBecomingUnblocked, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    disabled(schemaPath.forOneTimeEvents, ({ valueOf }) => !valueOf(schemaPath.sendChatMessages));
    /* eslint-enable @typescript-eslint/unbound-method */
  });

  constructor() {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved) {
      let parsed: ConnectScreenState | null = null;
      try {
        parsed = JSON.parse(saved) as unknown as ConnectScreenState;
      }
      catch {
        console.warn('Failed to parse saved connect screen state:', saved);
      }

      if (parsed) {
        this.model.set(parsed);
      }
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
}
