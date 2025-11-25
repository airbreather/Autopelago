import type { ResolveFn, Routes } from '@angular/router';

import { ConnectScreen } from './connect-screen/connect-screen';
import { type ConnectScreenState, connectScreenStateFromQueryParams } from './connect-screen/connect-screen-state';
import { GameScreen } from './game-screen/game-screen';

const resolveConnectScreenState = ((route) => {
  return connectScreenStateFromQueryParams(route.queryParamMap);
}) satisfies ResolveFn<ConnectScreenState>;

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    component: GameScreen,
    resolve: { connectScreenState: resolveConnectScreenState },
  },
];
