import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./connect-screen/connect-screen').then(m => m.ConnectScreen) },
  { path: 'game', loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen) },
];
