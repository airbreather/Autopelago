import { type ResolveFn, type Routes } from '@angular/router';

import { Client, type ConnectionOptions } from 'archipelago.js';

import { ConnectScreen } from './connect-screen/connect-screen';
import { BAKED_DEFINITIONS_BY_VICTORY_LANDMARK, VICTORY_LOCATION_NAME_LOOKUP } from './data/resolved-definitions';
import {
  type AutopelagoClientAndData,
  type AutopelagoSlotData,
  type AutopelagoStoredData,
  validateAutopelagoStoredData,
} from './data/slot-data';
import { AutopelagoService } from './game/autopelago';
import { GameStore } from './store/autopelago-store';

const connectResolve: ResolveFn<AutopelagoClientAndData> = async (route) => {
  const client = new Client();
  client.options.autoFetchDataPackage = false;
  let packageChecksum: string | null = null;
  client.socket.on('roomInfo', (packet) => {
    if ('Autopelago' in packet.datapackage_checksums) {
      packageChecksum = packet.datapackage_checksums['Autopelago'];
    }
  });

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

  const slotData = await client.login<AutopelagoSlotData>(
    `${host}:${port}`,
    slot,
    'Autopelago',
    options,
  );

  const player = client.players.self;
  const storedDataKey = `autopelago_${player.team.toString()}_${player.slot.toString()}`;
  let storedData: AutopelagoStoredData | null = null;
  try {
    storedData = await client.storage.fetch(storedDataKey);
    if (storedData && !validateAutopelagoStoredData(storedData)) {
      console.warn('invalid stored data', storedData, validateAutopelagoStoredData.errors);
      storedData = null;
    }
  }
  catch (err: unknown) {
    console.error('error fetching stored data', err);
    storedData = null;
  }

  if (!storedData) {
    const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];
    storedData = {
      foodFactor: 0,
      luckFactor: 0,
      energyFactor: 0,
      styleFactor: 0,
      distractionCounter: 0,
      startledCounter: 0,
      hasConfidence: false,
      mercyFactor: 0,
      sluggishCarryover: false,
      processedReceivedItemCount: 0,
      currentLocation: BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].startLocation,
      priorityPriorityLocations: [],
      priorityLocations: [],
    };
    await client.storage
      .prepare(storedDataKey, storedData)
      .replace(storedData)
      .commit(true);
  }

  return { client, slotData, storedData, storedDataKey, packageChecksum };
};

export const routes: Routes = [
  { path: '', component: ConnectScreen },
  {
    path: 'game',
    loadComponent: () => import('./game-screen/game-screen').then(m => m.GameScreen),
    providers: [GameStore, AutopelagoService],
    resolve: { game: connectResolve },
  },
  {
    path: 'headless',
    loadComponent: () => import('./headless/headless').then(m => m.Headless),
    providers: [GameStore],
    resolve: { game: connectResolve },
  },
];
