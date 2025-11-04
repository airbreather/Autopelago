import { DestroyRef, inject } from '@angular/core';
import { type ResolveFn, type Routes } from '@angular/router';
import { initializeClient } from './archipelago-client';

import { ConnectScreen } from './connect-screen/connect-screen';
import { type AutopelagoClientAndData } from './data/slot-data';
import { GameStore } from './store/autopelago-store';

const connectResolve: ResolveFn<AutopelagoClientAndData> = (route) => {
  const qp = route.queryParamMap;
  const host = qp.get('host');
  const port = Number(qp.get('port'));
  const slot = qp.get('slot');
  const password = qp.get('password');
  if (!(host && port && slot)) {
    throw new Error('Missing required query params. host, port, and slot must be provided!');
  }

  const destroyRef = inject(DestroyRef);
  return initializeClient({ host, port, slot, password, destroyRef });
};

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    providers: [GameStore],
    resolve: { game: connectResolve },
  },
  {
    path: 'headless',
    loadComponent: () => import('./headless/headless').then(m => m.Headless),
    providers: [GameStore],
    resolve: { game: connectResolve },
  },
];
