import type { DestroyRef } from '@angular/core';
import { Client, type ConnectionOptions } from 'archipelago.js';
import { BAKED_DEFINITIONS_BY_VICTORY_LANDMARK, VICTORY_LOCATION_NAME_LOOKUP } from './data/resolved-definitions';
import {
  type AutopelagoClientAndData,
  type AutopelagoSlotData,
  type AutopelagoStoredData,
  validateAutopelagoStoredData,
} from './data/slot-data';
import { createReactiveMessageLog } from './game/messages';

export interface InitializeClientOptions {
  host: string;
  port: number;
  slot: string;
  password: string | null;
  destroyRef: DestroyRef;
}

export async function initializeClient(initializeClientOptions: InitializeClientOptions): Promise<AutopelagoClientAndData> {
  const { host, port, slot, password, destroyRef } = initializeClientOptions;
  const client = new Client();
  // we want to wire up some stuff before fetching the data package:
  client.options.autoFetchDataPackage = false;
  // we have our own message log, so disable its own:
  client.options.maximumMessages = 0;
  let packageChecksum: string | null = null;
  client.socket.on('roomInfo', (packet) => {
    if ('Autopelago' in packet.datapackage_checksums) {
      packageChecksum = packet.datapackage_checksums['Autopelago'];
    }
  });
  const messageLog = createReactiveMessageLog(client, destroyRef);

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
    `${host}:${port.toString()}`,
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

  return { client, messageLog, slotData, storedData, storedDataKey, packageChecksum };
}
