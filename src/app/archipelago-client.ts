import { Injectable } from '@angular/core';
import { rxResource, takeUntilDestroyed } from "@angular/core/rxjs-interop";

import { BehaviorSubject, EMPTY, map, merge, mergeMap, Observable } from "rxjs";

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
  directHost: string;
  port: number;
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
  providedIn: 'root'
})
export class ArchipelagoClient {
  readonly #clientSubject = new BehaviorSubject<Client | null>(null);

  constructor() {
    this.events('messages', 'message')
      .pipe(takeUntilDestroyed())
      .subscribe(msg => {
        console.log('ANY OLD MESSAGE', msg);
      });
    this.events('messages', 'serverChat')
      .pipe(takeUntilDestroyed())
      .subscribe(msg => {
        console.log('SERVER CHAT', msg);
      });
  }

  isAuthenticated = rxResource({
    stream: () => merge(
      this.events('socket', 'connected').pipe(map(() => true)),
      this.events('socket', 'disconnected').pipe(map(() => false)),
    ),
  });

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

  async connect({ directHost, port, slot, password }: ConnectOptions) {
    const hostHasPort = /:\d+$/.test(directHost);
    const url = hostHasPort ? directHost : `${directHost}:${port.toString()}`;
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

  disconnect() {
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
