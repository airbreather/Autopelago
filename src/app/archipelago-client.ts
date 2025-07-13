import { Injectable } from '@angular/core';

import { BehaviorSubject, EMPTY, Observable } from 'rxjs';
import { mergeMap } from 'rxjs/operators';

import {
  Client,
  DeathEvents,
  EventBasedManager,
  ItemEvents,
  MessageEvents,
  PlayerEvents,
  RoomStateEvents,
  SocketEvents
} from 'archipelago.js';

export interface ConnectOptions {
  url: string;
  slot: string;
  password?: string;
}

// TODO: TypeScript isn't powerful enough to infer this fully, though the conditional type part is enough to let it give
// 'unknown' if we make any mistakes in the explicit part of this declaration.
type ClientManagerEventMap = {
  [K in keyof Client]: Client[K] extends EventBasedManager<infer E> ? E : never;
} & {
  socket: SocketEvents;
  room: RoomStateEvents;
  messages: MessageEvents;
  players: PlayerEvents;
  items: ItemEvents;
  deathLink: DeathEvents;
};

@Injectable({
  providedIn: 'any',
})
export class ArchipelagoClient {
  readonly #clientSubject = new BehaviorSubject<Client | null>(null);

  events<
    M extends keyof ClientManagerEventMap,
    E extends keyof ClientManagerEventMap[M] & string,
  >(
    managerName: M,
    eventName: E,
  ): Observable<ClientManagerEventMap[M][E]> {
    return this.#clientSubject.pipe(
      mergeMap(client => {
        if (client === null) {
          return EMPTY;
        }

        return new Observable<ClientManagerEventMap[M][E]>(subscriber => {
          const manager = client[managerName] as EventBasedManager<ClientManagerEventMap[M]>;
          const handler = (...args: ClientManagerEventMap[M][E]) => { subscriber.next(args); };
          manager.on(eventName, handler);
          return () => manager.off(eventName, handler);
        });
      }),
    );
  }

  async connect({ url, slot, password }: ConnectOptions): Promise<void> {
    try {
      // Disconnect any existing client
      this.disconnect();

      const client = new Client();
      this.#clientSubject.next(client);
      await client.login(url, slot, 'Autopelago', { password });
    } catch (error) {
      this.disconnect();
      throw error;
    }
  }

  disconnect(): void {
    const currentClient = this.#clientSubject.value;
    if (currentClient) {
      try {
        currentClient.socket.disconnect();
      } catch {
        // Ignore errors during disconnect
      }
    }
    this.#clientSubject.next(null);
  }
}
