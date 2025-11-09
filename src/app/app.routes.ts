import type { ResolveFn, Routes } from '@angular/router';

import { ConnectScreen } from './connect-screen/connect-screen';
import { ConnectScreenStore } from './store/connect-screen.store';

const resolveConnectScreenStore = ((route) => {
  const store = new ConnectScreenStore();
  store.initFromQueryParams(route.queryParamMap);
  return store;
}) satisfies ResolveFn<InstanceType<typeof ConnectScreenStore>>;

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    resolve: { connectScreenStore: resolveConnectScreenStore },
  },
];
