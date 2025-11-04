import { type DestroyRef, signal, type Signal } from '@angular/core';
import { Client, type ConnectionOptions, type GamePackage, type MessageNode } from 'archipelago.js';
import { List } from 'immutable';
import { BAKED_DEFINITIONS_BY_VICTORY_LANDMARK, VICTORY_LOCATION_NAME_LOOKUP } from './data/resolved-definitions';
import {
  type AutopelagoClientAndData,
  type AutopelagoSlotData,
  type AutopelagoStoredData,
  validateAutopelagoStoredData,
} from './data/slot-data';

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

  await loadPackage(client, packageChecksum);

  return { client, messageLog, slotData, storedData, storedDataKey, packageChecksum };
}

async function loadPackage(client: Client, packageChecksum: string | null): Promise<void> {
  if (packageChecksum) {
    const dataPackageStr = localStorage.getItem(packageChecksum);
    if (dataPackageStr) {
      try {
        client.package.importPackage({
          games: {
            Autopelago: JSON.parse(dataPackageStr) as GamePackage,
          },
        });
      }
      catch (e) {
        localStorage.removeItem(packageChecksum);
        console.error('error loading package', e);
      }
    }
  }

  if (client.package.findPackage('Autopelago')) {
    return;
  }

  const pkg = await client.package.fetchPackage(['Autopelago']);
  if ('Autopelago' in pkg.games) {
    const autopelagoPkg = pkg.games['Autopelago'];
    localStorage.setItem(autopelagoPkg.checksum, JSON.stringify(autopelagoPkg));
  }
}

export interface Message {
  ts: Date;
  nodes: readonly MessageNode[];
}

function createReactiveMessageLog(client: Client, destroyRef?: DestroyRef): Signal<List<Readonly<Message>>> {
  const messageLog = signal(List<Readonly<Message>>());
  const onMessage = (_text: string, nodes: readonly MessageNode[]) => {
    messageLog.update(messages => messages.push({
      ts: new Date(),
      nodes,
    }));
  };

  client.messages.on('message', onMessage);
  if (destroyRef) {
    destroyRef.onDestroy(() => {
      client.messages.off('message', onMessage);
    });
  }

  return messageLog.asReadonly();
}
