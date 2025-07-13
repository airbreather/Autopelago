import { inject } from "@angular/core";
import { Routes, UrlTree } from '@angular/router';

import { ConnectScreen } from "./connect-screen/connect-screen";
import { ArchipelagoClientWrapper } from "./archipelago-client-wrapper";

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    canActivate: [
      () => inject(ArchipelagoClientWrapper).isAuthenticated.value() ? true : new UrlTree(),
    ],
  },
];
