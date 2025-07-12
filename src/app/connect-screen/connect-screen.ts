import { Component, effect, ElementRef, inject, viewChild } from '@angular/core';
import { ConnectScreenStore, createHostSelector } from '../store/connect-screen.store';
import { ArchipelagoClientService } from '../services/archipelago-client.service';
import { takeUntilDestroyed } from "@angular/core/rxjs-interop";

@Component({
  selector: 'app-connect-screen',
  standalone: true,
  template: `
    <form #allInputs class="root" (submit)="onConnect($event)">
      <div class="inputs">
        <label for="slot">Slot:</label>
        <input #slotInput
               required
               id="slot"
               type="text"
               [value]="slot()"
               (input)="updateSlot(slotInput.value)" />
        <label for="host">Host:</label>
        <input #hostInput
               id="host"
               type="text"
               [value]="host()"
               (input)="updateDirectHost(hostInput.value)" />
        <label for="port">Port:</label>
        <input #portInput
               id="port"
               type="number"
               min="1"
               max="65535"
               [value]="port()"
               (input)="updatePort(portInput.valueAsNumber)" />
        <label for="password">Password:</label>
        <input #passwordInput
               id="password"
               type="password"
               [value]="password()"
               (input)="updatePassword(passwordInput.value)" />
        <fieldset class="inputs" [style.grid-column]="'span 2'">
          <legend>Time Between Steps (sec.)</legend>
          <label for="minTime">Minimum:</label>
          <input #minTimeInput
                 id="minTime"
                 type="number"
                 [value]="minTime()"
                 (input)="updateMinTime(minTimeInput.valueAsNumber)" />
          <label for="maxTime">Maximum:</label>
          <input #maxTimeInput
                 id="maxTime"
                 type="number"
                 [value]="maxTime()"
                 (input)="updateMaxTime(maxTimeInput.valueAsNumber)" />
        </fieldset>
      </div>
      <div class="inputs">
        <input #enableTileAnimationsInput
               id="enableTileAnimations"
               type="checkbox"
               [checked]="enableTileAnimations()"
               (input)="updateEnableTileAnimations(enableTileAnimationsInput.checked)" />
        <label for="enableTileAnimations">Enable tile animations</label>

        <input #enableRatAnimationsInput
               id="enableRatAnimations"
               type="checkbox"
               [checked]="enableRatAnimations()"
               (input)="updateEnableRatAnimations(enableRatAnimationsInput.checked)" />
        <label for="enableRatAnimations">Enable rat animations</label>

        <input #sendChatMessagesInput
               id="sendChatMessages"
               type="checkbox"
               [checked]="sendChatMessages()"
               (input)="updateSendChatMessages(sendChatMessagesInput.checked)" />
        <label for="sendChatMessages">Send chat messages...</label>
      </div>
      <div class="inputs" [style.padding-left]="'20px'">
        <input #whenTargetChangesInput
               id="whenTargetChanges"
               type="checkbox"
               [disabled]="!sendChatMessages()"
               [checked]="whenTargetChanges()"
               (input)="updateWhenTargetChanges(whenTargetChangesInput.checked)" />
        <label for="whenTargetChanges">when target changes</label>

        <input #whenBecomingBlockedInput
               id="whenBecomingBlocked"
               type="checkbox"
               [disabled]="!sendChatMessages()"
               [checked]="whenBecomingBlocked()"
               (input)="updateWhenBecomingBlocked(whenBecomingBlockedInput.checked)" />
        <label for="whenBecomingBlocked">when becoming blocked</label>

        <input #whenStillBlockedInput
               id="whenStillBlocked"
               type="checkbox"
               [disabled]="!(sendChatMessages() && whenBecomingBlocked())"
               [checked]="whenStillBlocked()"
               (input)="updateWhenStillBlocked(whenStillBlockedInput.checked)" />
        <label for="whenStillBlocked">when STILL blocked</label>

        <input #whenBecomingUnblockedInput
               id="whenBecomingUnblocked"
               type="checkbox"
               [disabled]="!sendChatMessages()"
               [checked]="whenBecomingUnblocked()"
               (input)="updateWhenBecomingUnblocked(whenBecomingUnblockedInput.checked)" />
        <label for="whenBecomingUnblocked">when becoming unblocked</label>

        <input #forOneTimeEventsInput
               id="forOneTimeEvents"
               type="checkbox"
               [disabled]="!sendChatMessages()"
               [checked]="forOneTimeEvents()"
               (input)="updateForOneTimeEvents(forOneTimeEventsInput.checked)" />
        <label for="forOneTimeEvents">for one-time events</label>
      </div>
      <input class="submit-button"
             type="submit"
             [disabled]="!allInputs.checkValidity()"
             value="Connect" />
    </form>
  `,
  styles: `
    .root {
      display: flex;
      flex-direction: column;
      margin: 5px;

      >:not(:first-child) {
        margin-top: 5px;
      }
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
  readonly #store = inject(ConnectScreenStore);
  readonly #archipelagoClient = inject(ArchipelagoClientService);

  _ = this.#archipelagoClient.message$.pipe(takeUntilDestroyed()).subscribe(message => {
    console.log(message);
  });

  // Expose store properties as getters for template access
  readonly slot = this.#store.slot;
  readonly port = this.#store.port;
  readonly password = this.#store.password;
  readonly minTime = this.#store.minTime;
  readonly maxTime = this.#store.maxTime;
  readonly enableTileAnimations = this.#store.enableTileAnimations;
  readonly enableRatAnimations = this.#store.enableRatAnimations;
  readonly sendChatMessages = this.#store.sendChatMessages;
  readonly whenTargetChanges = this.#store.whenTargetChanges;
  readonly whenBecomingBlocked = this.#store.whenBecomingBlocked;
  readonly whenStillBlocked = this.#store.whenStillBlocked;
  readonly whenBecomingUnblocked = this.#store.whenBecomingUnblocked;
  readonly forOneTimeEvents = this.#store.forOneTimeEvents;

  // Computed properties from selectors
  readonly host = createHostSelector(this.#store);

  protected readonly minTimeInput = viewChild<ElementRef<HTMLInputElement>>('minTimeInput');
  protected readonly maxTimeInput = viewChild<ElementRef<HTMLInputElement>>('maxTimeInput');

  __ = effect(() => {
    const minTimeInput = this.minTimeInput();
    const maxTimeInput = this.maxTimeInput();
    if (minTimeInput && maxTimeInput) {
      if (this.minTime() > this.maxTime()) {
        minTimeInput.nativeElement.setCustomValidity('Min cannot be greater than max.');
        maxTimeInput.nativeElement.setCustomValidity('Min cannot be greater than max.');
      } else {
        minTimeInput.nativeElement.setCustomValidity('');
        maxTimeInput.nativeElement.setCustomValidity('');
      }
    }
  });

  // Store update methods
  updateSlot = (value: string) => { this.#store.updateSlot(value); };
  updateDirectHost = (value: string) => { this.#store.updateDirectHost(value); };
  updatePassword = (value: string) => { this.#store.updatePassword(value); };
  updateMinTime = (value: number) => { this.#store.updateMinTime(value); };
  updateMaxTime = (value: number) => { this.#store.updateMaxTime(value); };
  updatePort = (value: number) => { this.#store.updatePort(value); };
  updateEnableTileAnimations = (value: boolean) => { this.#store.updateEnableTileAnimations(value); };
  updateEnableRatAnimations = (value: boolean) => { this.#store.updateEnableRatAnimations(value); };
  updateSendChatMessages = (value: boolean) => { this.#store.updateSendChatMessages(value); };
  updateWhenTargetChanges = (value: boolean) => { this.#store.updateWhenTargetChanges(value); };
  updateWhenBecomingBlocked = (value: boolean) => { this.#store.updateWhenBecomingBlocked(value); };
  updateWhenStillBlocked = (value: boolean) => { this.#store.updateWhenStillBlocked(value); };
  updateWhenBecomingUnblocked = (value: boolean) => { this.#store.updateWhenBecomingUnblocked(value); };
  updateForOneTimeEvents = (value: boolean) => { this.#store.updateForOneTimeEvents(value); };

  async onConnect(event: SubmitEvent) {
    event.preventDefault();
    try {
      await this.#archipelagoClient.connect();
      console.log('Successfully connected to Archipelago server!');
    } catch (error) {
      console.error('Failed to connect to Archipelago server:', error);
      // TODO: Show user-friendly error message
    }
  }
}
