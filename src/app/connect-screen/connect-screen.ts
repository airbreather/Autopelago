import {Component, computed, linkedSignal, signal, WritableSignal} from '@angular/core';

@Component({
  selector: 'app-connect-screen',
  standalone: true,
  template: `
    <form>
      <div>
        <label for="slot">Slot
          <input #slotInput
                 required
                 id="slot"
                 type="text"
                 [value]="slot()"
                 (input)="slot.set(slotInput.value)" />
        </label>
      </div>
      <div>
        <label for="host">Host
          <input #hostInput
                 id="host"
                 type="text"
                 [value]="host()"
                 (input)="directHost.set(hostInput.value)" />
        </label>
      </div>
      <div>
        <label for="port">Port
          <input #portInput
                 id="port"
                 type="number"
                 min="1"
                 max="65535"
                 [value]="port()"
                 (input)="port.set(portInput.valueAsNumber)" />
        </label>
      </div>
    </form>
  `,
  styles: `
    input:invalid {
      border: 2px dashed red;
    }

    input:valid {
      border: 2px solid black;
    }
  `
})
export class ConnectScreen {
  readonly slot = signal('');
  readonly directHost = signal('archipelago.gg:65535');
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
