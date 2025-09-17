import { rxResource } from '@angular/core/rxjs-interop';

import { BehaviorSubject, EMPTY, map, merge, mergeMap, Observable } from 'rxjs';

import {
  Client,
  type ConnectionOptions,
  type DeathEvents,
  EventBasedManager,
  type ItemEvents,
  type MessageEvents,
  type PlayerEvents,
  type RoomStateEvents,
  type SocketEvents,
} from 'archipelago.js';

export interface ConnectOptions {
  host: string;
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

export class ArchipelagoClient {
  readonly #clientSubject = new BehaviorSubject<Client | null>(null);

  isAuthenticated = rxResource({
    defaultValue: false,
    stream: () => merge(
      this.events('socket', 'connected').pipe(map(() => true)),
      this.events('socket', 'disconnected').pipe(map(() => false)),
    ),
  }).asReadonly();

  async say(message: string) {
    const client = this.#clientSubject.value;
    if (!client?.authenticated) {
      return false;
    }

    await client.messages.say(message);
    return true;
  }

  events<
    M extends keyof ClientManagerEventMap,
    E extends keyof ClientManagerEventMap[M] & string,
  >(
    managerName: M,
    eventName: E,
  ): Observable<ClientManagerEventMap[M][E]> {
    return this.#clientSubject.pipe(
      mergeMap((client) => {
        if (client === null) {
          return EMPTY;
        }

        return new Observable<ClientManagerEventMap[M][E]>((subscriber) => {
          const manager = client[managerName] as EventBasedManager<ClientManagerEventMap[M]>;
          const handler = (...args: ClientManagerEventMap[M][E]) => {
            subscriber.next(args);
          };
          manager.on(eventName, handler);
          return () => manager.off(eventName, handler);
        });
      }),
    );
  }

  async connect({ host, port, slot, password }: ConnectOptions) {
    const hostHasPort = /:\d+$/.test(host);
    const url = hostHasPort ? host : `${host}:${port.toString()}`;
    try {
      // Disconnect any existing client
      this.disconnect();

      const client = new Client();
      this.#clientSubject.next(client);
      const connectOptions: ConnectionOptions = password
        ? { password, slotData: true }
        : { slotData: true };
      await client.login(url, slot, 'Autopelago', connectOptions);
    }
    catch (error) {
      this.disconnect();
      throw error;
    }
  }

  disconnect() {
    const currentClient = this.#clientSubject.value;
    if (currentClient) {
      try {
        currentClient.socket.disconnect();
      }
      catch {
        // Ignore errors during disconnect
      }
    }
    this.#clientSubject.next(null);
  }
}
