import type { ResolveFn, Routes } from '@angular/router';
import type { InitializeClientOptions } from './archipelago-client';

import { ConnectScreen } from './connect-screen/connect-screen';

const resolveInitOptions: ResolveFn<Omit<InitializeClientOptions, 'destroyRef'>> = (route) => {
  const qp = route.queryParamMap;
  const host = qp.get('host');
  const port = Number(qp.get('port'));
  const slot = qp.get('slot');
  const password = qp.get('password');
  if (!(host && port && slot)) {
    throw new Error('Missing required query params. host, port, and slot must be provided!');
  }

  return { host, port, slot, password };
};

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    resolve: { initOptions: resolveInitOptions },
  },
];
