import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { type ActiveToast, ToastrService } from 'ngx-toastr';

import { toastError } from '../app-error-handler';
import { initializeClient } from '../archipelago-client';
import type { ConnectScreenState } from '../connect-screen/connect-screen-state';
import { GameStore } from '../store/autopelago-store';
import { GameScreenStore } from '../store/game-screen-store';
import { GameContent } from './game-content/game-content';

@Component({
  selector: 'app-game-screen',
  imports: [
    GameContent,
    RouterLink,
  ],
  providers: [
    GameScreenStore,
    GameStore,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #outer class="outer">
      @let loadedGame = game.value();
      @if (loadedGame) {
        @defer {
          <app-game-content [game]="loadedGame" />
        }
      }
      @else {
        <div class="disconnected">
          <h1>{{connectingMessage()}}</h1>
          <button class="return-button" routerLink="/">Back to Main Menu</button>
        </div>
      }
    </div>
  `,
  styles: `
    .outer {
      width: 100vw;
      height: 100vh;
    }

    .disconnected {
      width: 100%;
      height: 100%;
      display: flex;
      flex-direction: column;
      justify-content: center;
      align-items: center;
    }
  `,
})
export class GameScreen {
  readonly #destroyRef = inject(DestroyRef);
  readonly #toast = inject(ToastrService);
  #activeToast: ActiveToast<unknown> | null = null;
  readonly connectScreenState = input.required<ConnectScreenState>();
  protected readonly connectingMessage = signal('Connecting...');
  protected readonly game = resource({
    params: () => ({
      connectScreenState: this.connectScreenState(),
      destroyRef: this.#destroyRef,
    }),
    loader: async ({ params }) => {
      try {
        const result = await initializeClient(params);
        if (this.#activeToast !== null) {
          this.#toast.remove(this.#activeToast.toastId);
          this.#activeToast = null;
        }

        return result;
      }
      catch (err: unknown) {
        if (this.#activeToast !== null) {
          this.#toast.remove(this.#activeToast.toastId);
        }

        console.error('Error connecting when trying to connect:', err);
        this.#activeToast = toastError(this.#toast, err, { easeTime: 0 });
        return undefined;
      }
    },
  });

  constructor() {
    this.#destroyRef.onDestroy(() => {
      if (this.#activeToast !== null) {
        this.#toast.remove(this.#activeToast.toastId);
        this.#activeToast = null;
      }
    });
    let nextTimeoutDuration = 500;
    let prevTimeout = NaN;
    effect(() => {
      if (!Number.isNaN(prevTimeout)) {
        clearTimeout(prevTimeout);
        prevTimeout = NaN;
        nextTimeoutDuration = 500;
      }

      if (this.game.value()) {
        return;
      }

      if (this.game.error()) {
        prevTimeout = setTimeout(() => this.game.reload(), nextTimeoutDuration);
        nextTimeoutDuration = Math.min(nextTimeoutDuration * 1.3, 30000);
        return;
      }

      if (!this.game.isLoading()) {
        this.game.reload();
      }
    });
    effect(() => {
      const game = this.game.value();
      if (!game) {
        return;
      }

      this.connectingMessage.set('Disconnected! Trying to reconnect...');
      const { client } = game;
      client.socket.on('disconnected', () => {
        this.game.value.set(undefined);
        this.game.reload();
      });
    });
  }
}
