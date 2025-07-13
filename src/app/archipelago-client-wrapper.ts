import { inject, Injectable } from '@angular/core';
import { ArchipelagoClient } from "./archipelago-client";
import { rxResource, takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { map } from "rxjs/operators";

@Injectable({
  providedIn: 'root'
})
export class ArchipelagoClientWrapper {
  readonly #archipelagoClient = inject(ArchipelagoClient);

  constructor() {
    this.#archipelagoClient.message$.pipe(takeUntilDestroyed())
      .subscribe(msg => {
        console.log('ANY OLD MESSAGE', msg);
      });
    this.#archipelagoClient.serverChat$.pipe(takeUntilDestroyed())
      .subscribe(msg => {
        console.log('SERVER CHAT', msg);
      });
  }

  isAuthenticated = rxResource({
    stream: () =>
      this.#archipelagoClient.authenticatedClient$.pipe(map(c => c !== null)),
  });

  connect() {
    return this.#archipelagoClient.connect();
  }

  disconnect() {
    this.#archipelagoClient.disconnect();
  }
}
