import { type DestroyRef, signal, type Signal } from '@angular/core';
import BitArray from '@bitarray/typedarray';
import { Client, type ConnectionOptions, type MessageNode, type Player } from 'archipelago.js';
import { List } from 'immutable';
import type { ConnectScreenState } from './connect-screen/connect-screen-state';
import {
  type AutopelagoAura,
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
} from './data/resolved-definitions';
import type {
  AutopelagoClientAndData,
  AutopelagoSlotData,
  AutopelagoStoredData,
  UserRequestedLocation,
} from './data/slot-data';
import { trySetArrayProp, trySetBooleanProp, trySetNumberProp } from './hardened-state-propagation';

export interface InitializeClientOptions {
  connectScreenState: ConnectScreenState;
  destroyRef: DestroyRef;
}

const defaultStoredData = {
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
  currentLocation: 0,
  previousTargetLocationEvidence: null,
  auraDrivenLocations: Array<number>(0),
  userRequestedLocations: Array<UserRequestedLocation>(0),
} as const;

export async function initializeClient(initializeClientOptions: InitializeClientOptions): Promise<AutopelagoClientAndData> {
  const { connectScreenState, destroyRef } = initializeClientOptions;
  const { slot, host, port, password } = connectScreenState;
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
    `${host.replace(/:\d+$/, '')}:${port.toString()}`,
    slot,
    'Autopelago',
    options,
  );

  const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];
  const defs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
  const player = client.players.self;
  const storedDataKey = `autopelago_${player.team.toString()}_${player.slot.toString()}`;
  let retrievedStoredData: unknown;
  try {
    retrievedStoredData = await client.storage.fetch(storedDataKey);
  }
  catch (err: unknown) {
    console.error('error fetching stored data', err);
    retrievedStoredData = null;
  }

  const validatedStoredDataParts: Partial<AutopelagoStoredData> = { };
  if (typeof retrievedStoredData === 'object' && retrievedStoredData !== null) {
    trySetNumberProp(retrievedStoredData, 'foodFactor', validatedStoredDataParts, n => !Number.isNaN(n));
    trySetNumberProp(retrievedStoredData, 'luckFactor', validatedStoredDataParts, n => !Number.isNaN(n));
    trySetNumberProp(retrievedStoredData, 'energyFactor', validatedStoredDataParts, n => !Number.isNaN(n));
    trySetNumberProp(retrievedStoredData, 'styleFactor', validatedStoredDataParts, n => n >= 0);
    trySetNumberProp(retrievedStoredData, 'distractionCounter', validatedStoredDataParts, n => n >= 0 && n <= 3);
    trySetNumberProp(retrievedStoredData, 'startledCounter', validatedStoredDataParts, n => n >= 0 && n <= 3);
    trySetBooleanProp(retrievedStoredData, 'hasConfidence', validatedStoredDataParts);
    trySetNumberProp(retrievedStoredData, 'mercyFactor', validatedStoredDataParts, n => n >= 0);
    trySetBooleanProp(retrievedStoredData, 'sluggishCarryover', validatedStoredDataParts);
    trySetNumberProp(retrievedStoredData, 'processedReceivedItemCount', validatedStoredDataParts, n => n >= 0);
    trySetNumberProp(retrievedStoredData, 'currentLocation', validatedStoredDataParts, n => n >= 0 && n < defs.allLocations.length);
    trySetArrayProp(retrievedStoredData, 'auraDrivenLocations', validatedStoredDataParts, n => typeof n === 'number' && n >= 0 && n < defs.allLocations.length);
    trySetArrayProp(retrievedStoredData, 'userRequestedLocations', validatedStoredDataParts, n =>
      typeof n === 'object'
      && n !== null
      && 'location' in n
      && 'userSlot' in n
      && typeof n.location === 'number'
      && typeof n.userSlot === 'number'
      && n.location >= 0 && n.location < defs.allLocations.length
      && n.userSlot >= 0 && n.userSlot < client.players.teams[client.players.self.team].length,
    );
  }

  const storedData: AutopelagoStoredData = {
    ...defaultStoredData,
    currentLocation: defs.startLocation,
    ...validatedStoredDataParts,
  };

  if (retrievedStoredData === null) {
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
    connectScreenState,
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

interface BaseMessage {
  ts: Date;
  type: 'playerChat' | 'serverChat' | 'other';
  text: string;
  nodes: readonly MessageNode[];
}
export interface PlayerChatMessage extends BaseMessage {
  type: 'playerChat';
  player: Player;
}
export interface ServerChatMessage extends BaseMessage {
  type: 'serverChat';
}
export interface OtherMessage extends BaseMessage {
  type: 'other';
}
export type Message =
  | PlayerChatMessage
  | ServerChatMessage
  | OtherMessage
  ;

function createReactiveMessageLog(client: Client, destroyRef?: DestroyRef): Signal<List<Readonly<Message>>> {
  const specificMessagesAlreadyLogged = new Set<readonly MessageNode[]>();
  const messageLog = signal(List<Readonly<Message>>());
  function onMessage(message: Message) {
    messageLog.update(messages => messages.push(message));
  }
  function onPlayerChat(text: string, player: Player | undefined, nodes: readonly MessageNode[]) {
    const ts = new Date();
    if (player !== undefined) {
      onMessage({
        type: 'playerChat',
        ts,
        text,
        player,
        nodes,
      });
      specificMessagesAlreadyLogged.add(nodes);
    }
  }
  function onServerChat(text: string, nodes: readonly MessageNode[]) {
    const ts = new Date();
    onMessage({
      type: 'serverChat',
      ts,
      text,
      nodes,
    });
    specificMessagesAlreadyLogged.add(nodes);
  }
  function onAnyMessage(text: string, nodes: readonly MessageNode[]) {
    const ts = new Date();
    if (!specificMessagesAlreadyLogged.delete(nodes)) {
      onMessage({
        type: 'other',
        ts,
        text,
        nodes,
      });
    }
  }

  client.messages.on('chat', onPlayerChat);
  client.messages.on('serverChat', onServerChat);
  client.messages.on('message', onAnyMessage);
  if (destroyRef) {
    destroyRef.onDestroy(() => {
      client.messages.off('serverChat', onServerChat);
      client.messages.off('chat', onPlayerChat);
      client.messages.off('message', onAnyMessage);
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
