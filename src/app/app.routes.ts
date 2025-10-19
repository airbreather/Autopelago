import { type ResolveFn, type Routes } from '@angular/router';

import { Client, type ConnectionOptions } from 'archipelago.js';

import { ConnectScreen } from './connect-screen/connect-screen';
import type { AutopelagoSlotData } from './data/slot-data';
import { AutopelagoService } from './game/autopelago';

const connectResolve: ResolveFn<Client> = async (route) => {
  const archipelago = new Client();
  const qp = route.queryParamMap;
  const host = qp.get('host');
  const port = qp.get('port');
  const slot = qp.get('slot');
  const password = qp.get('password');
  if (!(host && port && slot)) {
    throw new Error('Missing required query params. host, port, and slot must be provided!');
  }

  let options: ConnectionOptions = {
    slotData: true,
    version: {
      major: 0,
      minor: 6,
      build: 2,
    },
  };
  if (password) {
    options = {
      ...options,
      password,
    };
  }

  const loginResult = await archipelago.login<AutopelagoSlotData>(
    `${host}:${port}`,
    slot,
    'Autopelago',
    options,
  );

  console.log(loginResult);
  return archipelago;
};

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    providers: [AutopelagoService],
    resolve: { autopelago: connectResolve },
  },
  {
    path: 'headless',
    loadComponent: () => import('./headless/headless').then(m => m.Headless),
    resolve: { archipelago: connectResolve },
  },
];
