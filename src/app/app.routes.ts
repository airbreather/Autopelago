import { inject } from '@angular/core';

import { type ResolveFn, type Routes } from '@angular/router';

import { type ConnectOptions } from './archipelago-client';
import { ConnectScreen } from './connect-screen/connect-screen';
import { AutopelagoService } from './game/autopelago';

const connectResolve: ResolveFn<AutopelagoService> = async (route) => {
  const autopelago = inject(AutopelagoService);
  if (autopelago.rawClient.isAuthenticated.value()) {
    return autopelago;
  }

  const qp = route.queryParamMap;
  const host = qp.get('host');
  const port = qp.get('port');
  const slot = qp.get('slot');
  const password = qp.get('password');
  if (!(host && port && slot)) {
    throw new Error('Missing required query params. host, port, and slot must be provided!');
  }

  const options: ConnectOptions = { host, port: Number(port), slot };
  if (password) {
    options.password = password;
  }

  await autopelago.connect(options);
  return autopelago;
};

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    providers: [AutopelagoService],
    resolve: { autopelago: connectResolve },
  },
];
