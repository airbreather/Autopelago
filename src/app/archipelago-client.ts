import { type DestroyRef, signal, type Signal } from '@angular/core';
import BitArray from '@bitarray/typedarray';
import { Client, type ConnectionOptions, type MessageNode } from 'archipelago.js';
import { List } from 'immutable';
import {
  type AutopelagoAura,
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
} from './data/resolved-definitions';
import type { AutopelagoClientAndData, AutopelagoSlotData, AutopelagoStoredData } from './data/slot-data';
import type { ConnectScreenStore } from './store/connect-screen.store';

export interface InitializeClientOptions {
  connectScreenStore: InstanceType<typeof ConnectScreenStore>;
  destroyRef: DestroyRef;
}

export async function initializeClient(initializeClientOptions: InitializeClientOptions): Promise<AutopelagoClientAndData> {
  const { connectScreenStore, destroyRef } = initializeClientOptions;
  const slot = connectScreenStore.slot();
  const host = connectScreenStore.host().replace(/:\d+$/, '');
  const port = connectScreenStore.port();
  const password = connectScreenStore.password();
  const client = new Client();
  destroyRef.onDestroy(() => {
    client.socket.disconnect();
  });

  // we have our own message log, so disable its own:
  client.options.maximumMessages = 0;
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

  const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];
  const defs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
  const player = client.players.self;
  const storedDataKey = `autopelago_${player.team.toString()}_${player.slot.toString()}`;
  let storedData: AutopelagoStoredData | null;
  try {
    storedData = await client.storage.fetch(storedDataKey);
  }
  catch (err: unknown) {
    console.error('error fetching stored data', err);
    storedData = null;
  }

  if (storedData === null) {
    storedData = {
      workDone: NaN,
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
      currentLocation: defs.startLocation,
      previousTargetLocationEvidence: null,
      auraDrivenLocations: [],
      userRequestedLocations: [],
    };
    await client.storage
      .prepare(storedDataKey, storedData)
      .replace(storedData)
      .commit(true);
  }

  const pkg = client.package.findPackage('Autopelago');
  if (!pkg) {
    throw new Error('Autopelago package not found');
  }

  const locationNetworkNameLookup = pkg.locationTable;
  const items = await client.scout(defs.allLocations.map(l => locationNetworkNameLookup[l.name]));
  const locationIsProgression = new BitArray(defs.allLocations.length);
  const locationIsTrap = new BitArray(defs.allLocations.length);
  for (const item of items) {
    if (item.progression) {
      locationIsProgression[defs.locationNameLookup.get(item.locationName) ?? -1] = 1;
    }

    if (item.trap) {
      locationIsTrap[defs.locationNameLookup.get(item.locationName) ?? -1] = 1;
    }
  }

  return {
    connectScreenStore,
    client,
    pkg,
    resolvedItems: resolveItems(slotData),
    messageLog,
    slotData,
    locationIsProgression,
    locationIsTrap,
    storedData,
    storedDataKey,
  };
}

export interface Message {
  ts: Date;
  nodes: readonly MessageNode[];
}

function createReactiveMessageLog(client: Client, destroyRef?: DestroyRef): Signal<List<Readonly<Message>>> {
  const messageLog = signal(List<Readonly<Message>>());
  function onMessage(_text: string, nodes: readonly MessageNode[]) {
    messageLog.update(messages => messages.push({
      ts: new Date(),
      nodes,
    }));
  }

  client.messages.on('message', onMessage);
  if (destroyRef) {
    destroyRef.onDestroy(() => {
      client.messages.off('message', onMessage);
    });
  }

  return messageLog.asReadonly();
}

function resolveItems(slotData: AutopelagoSlotData) {
  const enabledAuras = new Set<AutopelagoAura>([...slotData.enabled_buffs, ...slotData.enabled_traps]);
  return BAKED_DEFINITIONS_FULL.allItems.map(item => ({
    ...item,
    lactoseAwareName: slotData.lactose_intolerant ? item.lactoseIntolerantName : item.lactoseName,
    enabledAurasGranted: item.aurasGranted.filter(a => enabledAuras.has(a)),
  }));
}
