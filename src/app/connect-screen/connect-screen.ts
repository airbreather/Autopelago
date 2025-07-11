import {Component, computed, linkedSignal, signal, WritableSignal} from '@angular/core';

@Component({
  selector: 'app-connect-screen',
  standalone: true,
  template: `
    <div class="inputs">
      <label for="slot">Slot:</label>
      <input #slotInput
             required
             id="slot"
             type="text"
             [value]="slot()"
             (input)="slot.set(slotInput.value)" />
      <label for="host">Host:</label>
      <input #hostInput
             id="host"
             type="text"
             [value]="host()"
             (input)="directHost.set(hostInput.value)" />
      <label for="port">Port:</label>
      <input #portInput
             id="port"
             type="number"
             min="1"
             max="65535"
             [value]="port()"
             (input)="port.set(portInput.valueAsNumber)" />
      <label for="password">Password:</label>
      <input #passwordInput
             id="password"
             type="password"
             [value]="password()"
             (input)="password.set(passwordInput.value)" />
      <fieldset class="inputs" [style.grid-column]="'span 2'">
        <legend>Time Between Steps (sec.)</legend>
        <label for="minTime">Minimum:</label>
        <input #minTimeInput
               id="minTime"
               type="number"
               [value]="minTime()"
               (input)="minTime.set(minTimeInput.valueAsNumber)" />
        <label for="maxTime">Maximum:</label>
        <input #maxTimeInput
               id="maxTime"
               type="number"
               [value]="maxTime()"
               (input)="maxTime.set(maxTimeInput.valueAsNumber)" />
      </fieldset>
    </div>
    <div class="inputs">
      <input #enableTileAnimationsInput
             id="enableTileAnimations"
             type="checkbox"
             [checked]="enableTileAnimations()"
             (input)="enableTileAnimations.set(enableTileAnimationsInput.checked)" />
      <label for="enableTileAnimations">Enable tile animations</label>

      <input #enableRatAnimationsInput
             id="enableRatAnimations"
             type="checkbox"
             [checked]="enableRatAnimations()"
             (input)="enableRatAnimations.set(enableRatAnimationsInput.checked)" />
      <label for="enableRatAnimations">Enable rat animations</label>

      <input #sendChatMessagesInput
             id="sendChatMessages"
             type="checkbox"
             [checked]="sendChatMessages()"
             (input)="sendChatMessages.set(sendChatMessagesInput.checked)" />
      <label for="sendChatMessages">Send chat messages...</label>
    </div>
    <div class="inputs" [style.padding-left]="'20px'">
      <input #whenTargetChangesInput
             id="whenTargetChanges"
             type="checkbox"
             [disabled]="!sendChatMessages()"
             [checked]="whenTargetChanges()"
             (input)="whenTargetChanges.set(whenTargetChangesInput.checked)" />
      <label for="whenTargetChanges">when target changes</label>

      <input #whenBecomingBlockedInput
             id="whenBecomingBlocked"
             type="checkbox"
             [disabled]="!sendChatMessages()"
             [checked]="whenBecomingBlocked()"
             (input)="whenBecomingBlocked.set(whenBecomingBlockedInput.checked)" />
      <label for="whenBecomingBlocked">when becoming blocked</label>

      <input #whenStillBlockedInput
             id="whenStillBlocked"
             type="checkbox"
             [disabled]="!(sendChatMessages() && whenBecomingBlocked())"
             [checked]="whenStillBlocked()"
             (input)="whenStillBlocked.set(whenStillBlockedInput.checked)" />
      <label for="whenStillBlocked">when STILL blocked</label>

      <input #whenBecomingUnblockedInput
             id="whenBecomingUnblocked"
             type="checkbox"
             [disabled]="!sendChatMessages()"
             [checked]="whenBecomingUnblocked()"
             (input)="whenBecomingUnblocked.set(whenBecomingUnblockedInput.checked)" />
      <label for="whenBecomingUnblocked">when becoming unblocked</label>

      <input #forOneTimeEventsInput
             id="forOneTimeEvents"
             type="checkbox"
             [disabled]="!sendChatMessages()"
             [checked]="forOneTimeEvents()"
             (input)="forOneTimeEvents.set(forOneTimeEventsInput.checked)" />
      <label for="forOneTimeEvents">for one-time events</label>
    </div>
  `,
  styles: `
    div:not(:first-child) {
      padding-top: 5px;
    }

    .inputs {
      display: grid;
      gap: calc(5rem/16);
      grid-template-columns: max-content 1fr;
      
      label {
        align-self: center;
      }
    }
  `
})
export class ConnectScreen {
  readonly slot = signal('');
  readonly directHost = signal('archipelago.gg');
  readonly host = linkedSignal({
    source: () => ({
      direct: this.directHost(),
      port: this.port(),
      portFromHost: this.portFromHost(),
    }),
    computation: (source) => {
      return source.portFromHost == source.port
        ? source.direct
        : source.direct.replace(/(:\d+)$/, '');
    },
  });
  readonly portFromHost = computed(() => {
    const m = /(?<=:)\d+$/.exec(this.directHost());
    return m ? Number(m[0]) : null;
  });
  readonly port: WritableSignal<number> = linkedSignal({
    source: () => this.portFromHost(),
    computation: (source, previous) => {
      if (source) {
        return source;
      }

      if (previous) {
        return previous.value;
      }

      return 65535;
    },
  });
  readonly password = signal('');
  readonly minTime = signal(20);
  readonly maxTime = signal(30);
  readonly enableTileAnimations = signal(true);
  readonly enableRatAnimations = signal(true);
  readonly sendChatMessages = signal(true);
  readonly whenTargetChanges = signal(true);
  readonly whenBecomingBlocked = signal(true);
  readonly whenStillBlocked = signal(false);
  readonly whenBecomingUnblocked = signal(true);
  readonly forOneTimeEvents = signal(true);


}
