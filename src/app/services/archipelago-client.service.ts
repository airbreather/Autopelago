import { Injectable, inject } from '@angular/core';

import { BehaviorSubject, distinctUntilChanged, Observable } from 'rxjs';
import { filter, mergeMap } from 'rxjs/operators';

import { Client } from 'archipelago.js';

import type {
  PlayerEvents,
  ItemEvents,
  MessageEvents,
  DeathEvents,
  RoomStateEvents,
  SocketEvents
} from 'archipelago.js';

import { ConnectScreenStore } from '../store/connect-screen.store';

@Injectable({
  providedIn: 'root'
})
export class ArchipelagoClientService {
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
  readonly playerAliasUpdated$ = this.#createAuthenticatedEventObservable<PlayerEvents['aliasUpdated']>('players', 'aliasUpdated');

  // Item Events (require authentication)
  readonly itemsReceived$ = this.#createAuthenticatedEventObservable<ItemEvents['itemsReceived']>('items', 'itemsReceived');
  readonly hintReceived$ = this.#createAuthenticatedEventObservable<ItemEvents['hintReceived']>('items', 'hintReceived');
  readonly hintFound$ = this.#createAuthenticatedEventObservable<ItemEvents['hintFound']>('items', 'hintFound');
  readonly hintsInitialized$ = this.#createAuthenticatedEventObservable<ItemEvents['hintsInitialized']>('items', 'hintsInitialized');

  // Message Events
  readonly message$ = this.#createAuthenticatedEventObservable<MessageEvents['message']>('messages', 'message');
  readonly itemSent$ = this.#createAuthenticatedEventObservable<MessageEvents['itemSent']>('messages', 'itemSent');
  readonly itemCheated$ = this.#createAuthenticatedEventObservable<MessageEvents['itemCheated']>('messages', 'itemCheated');
  readonly itemHinted$ = this.#createAuthenticatedEventObservable<MessageEvents['itemHinted']>('messages', 'itemHinted');
  // Connection events can happen before authentication, so use regular observable
  readonly connected$ = this.#createEventObservable<MessageEvents['connected']>('messages', 'connected');
  readonly disconnected$ = this.#createEventObservable<MessageEvents['disconnected']>('messages', 'disconnected');
  // Chat and other authenticated events
  readonly chat$ = this.#createAuthenticatedEventObservable<MessageEvents['chat']>('messages', 'chat');
  readonly serverChat$ = this.#createAuthenticatedEventObservable<MessageEvents['serverChat']>('messages', 'serverChat');
  readonly tutorial$ = this.#createAuthenticatedEventObservable<MessageEvents['tutorial']>('messages', 'tutorial');
  readonly tagsUpdated$ = this.#createAuthenticatedEventObservable<MessageEvents['tagsUpdated']>('messages', 'tagsUpdated');
  readonly userCommand$ = this.#createAuthenticatedEventObservable<MessageEvents['userCommand']>('messages', 'userCommand');
  readonly adminCommand$ = this.#createAuthenticatedEventObservable<MessageEvents['adminCommand']>('messages', 'adminCommand');
  readonly goaled$ = this.#createAuthenticatedEventObservable<MessageEvents['goaled']>('messages', 'goaled');
  readonly released$ = this.#createAuthenticatedEventObservable<MessageEvents['released']>('messages', 'released');
  readonly collected$ = this.#createAuthenticatedEventObservable<MessageEvents['collected']>('messages', 'collected');
  readonly countdown$ = this.#createAuthenticatedEventObservable<MessageEvents['countdown']>('messages', 'countdown');

  // Death Events (require authentication)
  readonly deathReceived$ = this.#createAuthenticatedEventObservable<DeathEvents['deathReceived']>('deathLink', 'deathReceived');

  // Room State Events (require authentication)
  readonly passwordUpdated$ = this.#createAuthenticatedEventObservable<RoomStateEvents['passwordUpdated']>('room', 'passwordUpdated');
  readonly permissionsUpdated$ = this.#createAuthenticatedEventObservable<RoomStateEvents['permissionsUpdated']>('room', 'permissionsUpdated');
  readonly locationCheckPointsUpdated$ = this.#createAuthenticatedEventObservable<RoomStateEvents['locationCheckPointsUpdated']>('room', 'locationCheckPointsUpdated');
  readonly hintCostUpdated$ = this.#createAuthenticatedEventObservable<RoomStateEvents['hintCostUpdated']>('room', 'hintCostUpdated');
  readonly hintPointsUpdated$ = this.#createAuthenticatedEventObservable<RoomStateEvents['hintPointsUpdated']>('room', 'hintPointsUpdated');
  readonly locationsChecked$ = this.#createAuthenticatedEventObservable<RoomStateEvents['locationsChecked']>('room', 'locationsChecked');

  // Socket Events
  readonly bounced$ = this.#createEventObservable<SocketEvents['bounced']>('socket', 'bounced');
  readonly socketConnected$ = this.#createEventObservable<SocketEvents['connected']>('socket', 'connected');
  readonly connectionRefused$ = this.#createEventObservable<SocketEvents['connectionRefused']>('socket', 'connectionRefused');
  readonly dataPackage$ = this.#createEventObservable<SocketEvents['dataPackage']>('socket', 'dataPackage');
  readonly invalidPacket$ = this.#createEventObservable<SocketEvents['invalidPacket']>('socket', 'invalidPacket');
  readonly locationInfo$ = this.#createEventObservable<SocketEvents['locationInfo']>('socket', 'locationInfo');
  readonly printJSON$ = this.#createEventObservable<SocketEvents['printJSON']>('socket', 'printJSON');
  readonly receivedItems$ = this.#createEventObservable<SocketEvents['receivedItems']>('socket', 'receivedItems');
  readonly retrieved$ = this.#createEventObservable<SocketEvents['retrieved']>('socket', 'retrieved');
  readonly roomInfo$ = this.#createEventObservable<SocketEvents['roomInfo']>('socket', 'roomInfo');
  readonly roomUpdate$ = this.#createEventObservable<SocketEvents['roomUpdate']>('socket', 'roomUpdate');
  readonly setReply$ = this.#createEventObservable<SocketEvents['setReply']>('socket', 'setReply');
  readonly receivedPacket$ = this.#createEventObservable<SocketEvents['receivedPacket']>('socket', 'receivedPacket');
  readonly sentPackets$ = this.#createEventObservable<SocketEvents['sentPackets']>('socket', 'sentPackets');
  readonly socketDisconnected$ = this.#createEventObservable<SocketEvents['disconnected']>('socket', 'disconnected');

  /**
   * Creates an RxJS observable for a specific event from a specific manager.
   * This method accepts the observable to start from, allowing for different client states
   * (connected vs authenticated). The observable emits Client | null to notify on disconnect,
   * allowing the mergeMap operator to unsubscribe previous Client's events when set to null.
   * Observables never terminate to allow reconnection.
   */
  #createEventObservableFromSource<T extends unknown[]>(
    sourceObservable: Observable<Client | null>,
    managerName: keyof Client,
    eventName: string
  ): Observable<T> {
    return sourceObservable.pipe(
      mergeMap(client => {
        if (client === null) {
          // When client is null (disconnected), return empty observable
          // This allows the mergeMap to unsubscribe from previous client's events
          return new Observable<T>(subscriber => {
            // Empty observable that completes immediately
            subscriber.complete();
          });
        }

        const manager = client[managerName] as { on: (event: string, listener: (...args: T) => void) => void; off: (event: string, listener: (...args: T) => void) => void };

        return new Observable<T>(subscriber => {
          const listener = (...args: T) => {
            subscriber.next(args);
          };

          manager.on(eventName, listener);

          return () => {
            manager.off(eventName, listener);
          };
        });
      }),
    );
  }

  /**
   * Helper method to create event observables that work with any connected client
   */
  #createEventObservable<T extends unknown[]>(
    managerName: keyof Client,
    eventName: string
  ): Observable<T> {
    return this.#createEventObservableFromSource(this.client$, managerName, eventName);
  }

  /**
   * Helper method to create event observables that require an authenticated client
   */
  #createAuthenticatedEventObservable<T extends unknown[]>(
    managerName: keyof Client,
    eventName: string
  ): Observable<T> {
    return this.#createEventObservableFromSource(this.authenticatedClient$, managerName, eventName);
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

      // re-emit for the sake of
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
