import { inject } from "@angular/core";
import { Routes, UrlTree } from '@angular/router';

import { ConnectScreen } from "./connect-screen/connect-screen";
import { ArchipelagoClient } from "./archipelago-client";

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    canActivate: [
      () => inject(ArchipelagoClient).isAuthenticated.value() ? true : new UrlTree(),
    ],
  },
];
