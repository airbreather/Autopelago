import { DestroyRef, inject } from '@angular/core';
import type { ResolveFn, Routes } from '@angular/router';

import { ConnectScreen } from './connect-screen/connect-screen';
import type { AutopelagoClientAndData } from './data/slot-data';

const connectResolve: ResolveFn<AutopelagoClientAndData> = (route) => {
  const destroyRef = inject(DestroyRef);
  return import('./archipelago-client').then((m) => {
    const qp = route.queryParamMap;
    const host = qp.get('host');
    const port = Number(qp.get('port'));
    const slot = qp.get('slot');
    const password = qp.get('password');
    if (!(host && port && slot)) {
      throw new Error('Missing required query params. host, port, and slot must be provided!');
    }

    return m.initializeClient({ host, port, slot, password, destroyRef });
  });
};

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    resolve: { game: connectResolve },
  },
  {
    path: 'headless',
    loadComponent: () => import('./headless/headless').then(m => m.Headless),
    resolve: { game: connectResolve },
  },
];
