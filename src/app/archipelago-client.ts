import { Injectable, inject } from '@angular/core';

import { BehaviorSubject, distinctUntilChanged, EMPTY, Observable } from 'rxjs';
import { filter, mergeMap } from 'rxjs/operators';

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

import { ConnectScreenStore } from './store/connect-screen.store';

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
  readonly #connectScreenStore = inject(ConnectScreenStore);

  /**
   * The main client subject. This is the only place where a Subject is used directly,
   * as recommended in the requirements. All other observables are derived from this
   * using RxJS pipelines.
   */
  readonly #clientSubject = new BehaviorSubject<Client | null>(null);

  /**
   * Observable that emits the current client instance when available
   */
  readonly client$ = this.#clientSubject.pipe(
    distinctUntilChanged(),
  );

  /**
   * Observable that emits only when client is connected and authenticated
   */
  readonly authenticatedClient$ = this.#clientSubject.pipe(
    filter(client => client === null || client.authenticated),
    distinctUntilChanged(),
  );

  // Player Events (require authentication)
  readonly playerAliasUpdated$ = this.#observe(this.authenticatedClient$, 'players', 'aliasUpdated');

  // Item Events (require authentication)
  readonly itemsReceived$ = this.#observe(this.authenticatedClient$, 'items', 'itemsReceived');
  readonly hintReceived$ = this.#observe(this.authenticatedClient$, 'items', 'hintReceived');
  readonly hintFound$ = this.#observe(this.authenticatedClient$, 'items', 'hintFound');
  readonly hintsInitialized$ = this.#observe(this.authenticatedClient$, 'items', 'hintsInitialized');

  // Message Events
  readonly message$ = this.#observe(this.authenticatedClient$, 'messages', 'message');
  readonly itemSent$ = this.#observe(this.authenticatedClient$, 'messages', 'itemSent');
  readonly itemCheated$ = this.#observe(this.authenticatedClient$, 'messages', 'itemCheated');
  readonly itemHinted$ = this.#observe(this.authenticatedClient$, 'messages', 'itemHinted');
  // Connection events can happen before authentication, so use regular observable
  readonly connected$ = this.#observe(this.client$, 'messages', 'connected');
  readonly disconnected$ = this.#observe(this.client$, 'messages', 'disconnected');
  // Chat and other authenticated events
  readonly chat$ = this.#observe(this.authenticatedClient$, 'messages', 'chat');
  readonly serverChat$ = this.#observe(this.authenticatedClient$, 'messages', 'serverChat');
  readonly tutorial$ = this.#observe(this.authenticatedClient$, 'messages', 'tutorial');
  readonly tagsUpdated$ = this.#observe(this.authenticatedClient$, 'messages', 'tagsUpdated');
  readonly userCommand$ = this.#observe(this.authenticatedClient$, 'messages', 'userCommand');
  readonly adminCommand$ = this.#observe(this.authenticatedClient$, 'messages', 'adminCommand');
  readonly goaled$ = this.#observe(this.authenticatedClient$, 'messages', 'goaled');
  readonly released$ = this.#observe(this.authenticatedClient$, 'messages', 'released');
  readonly collected$ = this.#observe(this.authenticatedClient$, 'messages', 'collected');
  readonly countdown$ = this.#observe(this.authenticatedClient$, 'messages', 'countdown');

  // Death Events (require authentication)
  readonly deathReceived$ = this.#observe(this.authenticatedClient$, 'deathLink', 'deathReceived');

  // Room State Events (require authentication)
  readonly passwordUpdated$ = this.#observe(this.authenticatedClient$, 'room', 'passwordUpdated');
  readonly permissionsUpdated$ = this.#observe(this.authenticatedClient$, 'room', 'permissionsUpdated');
  readonly locationCheckPointsUpdated$ = this.#observe(this.authenticatedClient$, 'room', 'locationCheckPointsUpdated');
  readonly hintCostUpdated$ = this.#observe(this.authenticatedClient$, 'room', 'hintCostUpdated');
  readonly hintPointsUpdated$ = this.#observe(this.authenticatedClient$, 'room', 'hintPointsUpdated');
  readonly locationsChecked$ = this.#observe(this.authenticatedClient$, 'room', 'locationsChecked');

  // Socket Events
  readonly bounced$ = this.#observe(this.client$, 'socket', 'bounced');
  readonly socketConnected$ = this.#observe(this.client$, 'socket', 'connected');
  readonly connectionRefused$ = this.#observe(this.client$, 'socket', 'connectionRefused');
  readonly dataPackage$ = this.#observe(this.client$, 'socket', 'dataPackage');
  readonly invalidPacket$ = this.#observe(this.client$, 'socket', 'invalidPacket');
  readonly locationInfo$ = this.#observe(this.client$, 'socket', 'locationInfo');
  readonly printJSON$ = this.#observe(this.client$, 'socket', 'printJSON');
  readonly receivedItems$ = this.#observe(this.client$, 'socket', 'receivedItems');
  readonly retrieved$ = this.#observe(this.client$, 'socket', 'retrieved');
  readonly roomInfo$ = this.#observe(this.client$, 'socket', 'roomInfo');
  readonly roomUpdate$ = this.#observe(this.client$, 'socket', 'roomUpdate');
  readonly setReply$ = this.#observe(this.client$, 'socket', 'setReply');
  readonly receivedPacket$ = this.#observe(this.client$, 'socket', 'receivedPacket');
  readonly sentPackets$ = this.#observe(this.client$, 'socket', 'sentPackets');
  readonly socketDisconnected$ = this.#observe(this.client$, 'socket', 'disconnected');

  /**
   * Creates an RxJS observable for a specific event from a specific manager.
   * This method accepts the observable to start from, allowing for different client states
   * (connected vs authenticated). The observable emits Client | null to notify on disconnect,
   * allowing the mergeMap operator to unsubscribe previous Client's events when set to null.
   * Observables never terminate to allow reconnection.
   */
  #observe<
    M extends keyof ClientManagerEventMap,
    E extends keyof ClientManagerEventMap[M] & string,
  >(
    sourceObservable: Observable<Client | null>,
    managerName: M,
    eventName: E,
  ): Observable<ClientManagerEventMap[M][E]> {
    return sourceObservable.pipe(
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

  /**
   * Connects to an Archipelago server using the parameters from ConnectScreenStore.
   * This method should be called when the connect form is submitted.
   */
  async connect(): Promise<void> {
    try {
      // Disconnect any existing client
      this.disconnect();

      // Create new client
      const client = new Client();

      // Get connection parameters from store
      const directHost = this.#connectScreenStore.directHost();
      const port = this.#connectScreenStore.port();
      const slot = this.#connectScreenStore.slot();
      const password = this.#connectScreenStore.password();

      // Build URL - check if port is already in host string
      // Don't include protocol to let archipelago.js handle wss/ws fallback
      const hostHasPort = /:\d+$/.test(directHost);
      const url = hostHasPort ? directHost : `${directHost}:${port.toString()}`;

      // Set the client in the subject before connecting so subscribers can listen to events
      this.#clientSubject.next(client);

      // Connect and authenticate
      await client.login(url, slot, 'Autopelago', { password });

      // re-emit for the sake of the observable that filters out Clients that aren't authenticated.
      this.#clientSubject.next(client);
    } catch (error) {
      // If connection fails, clear the client
      this.disconnect();
      throw error;
    }
  }

  /**
   * Disconnects from the current Archipelago server and clears the client.
   */
  disconnect(): void {
    console.log('Disconnecting...');
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

  isAuthenticated() {
    return !!this.#clientSubject.value?.authenticated;
  }
}
