import { inject, Injectable } from '@angular/core';
import { rxResource, takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { map } from "rxjs/operators";

import { ArchipelagoClient } from "./archipelago-client";
import { merge } from "rxjs";

export interface ConnectOptions {
  directHost: string;
  port: number;
  slot: string;
  password?: string;
}

@Injectable({
  providedIn: 'root'
})
export class ArchipelagoClientWrapper {
  readonly #archipelagoClient = inject(ArchipelagoClient);

  constructor() {
    this.#archipelagoClient.events('messages', 'message')
      .pipe(takeUntilDestroyed())
      .subscribe(msg => {
        console.log('ANY OLD MESSAGE', msg);
      });
    this.#archipelagoClient.events('messages', 'serverChat')
      .pipe(takeUntilDestroyed())
      .subscribe(msg => {
        console.log('SERVER CHAT', msg);
      });
  }

  isAuthenticated = rxResource({
    stream: () => merge(
      this.#archipelagoClient.events('socket', 'connected'),
      this.#archipelagoClient.events('socket', 'disconnected'),
    ).pipe(map(pkt => pkt.length > 0)),
  });

  connect({ directHost, port, slot, password }: ConnectOptions) {
    const hostHasPort = /:\d+$/.test(directHost);
    const url = hostHasPort ? directHost : `${directHost}:${port.toString()}`;
    return this.#archipelagoClient.connect({ url, slot, password });
  }

  disconnect() {
    this.#archipelagoClient.disconnect();
  }
}
