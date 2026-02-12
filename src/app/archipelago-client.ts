import {
  Client,
  type ClientStatus,
  clientStatuses,
  type ConnectionOptions,
  type DataChangeCallback,
  Hint,
  type JSONSerializable,
  type MessageNode,
  type Player,
} from '@airbreather/archipelago.js';
import { computed, type DestroyRef, signal, type Signal } from '@angular/core';
import BitArray from '@bitarray/typedarray';
import { List, Repeat } from 'immutable';
import type { ConnectScreenState } from './connect-screen/connect-screen-state';
import {
  type AutopelagoItem,
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  VICTORY_LOCATION_NAME_LOOKUP,
} from './data/resolved-definitions';
import type {
  AutopelagoClientAndData,
  AutopelagoSlotData,
  AutopelagoStoredData,
  UserRequestedLocation,
} from './data/slot-data';
import {
  trySetArrayProp,
  trySetBooleanProp,
  trySetNullableNumberProp,
  trySetNumberProp,
} from './utils/hardened-state-propagation';

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

  // we also do our own thing to monitor player status
  const playersWithStatus = createReactivePlayerList(client);

  // we also have our own hint stuff.
  const reactiveHints = createReactiveHints(client, destroyRef);

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
    trySetNullableNumberProp(retrievedStoredData, 'hyperFocusLocation', validatedStoredDataParts, n => n >= 0 && n < defs.allLocations.length);
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
  const locationNetworkIdToLocation: Readonly<Record<number, number>> = Object.fromEntries(defs.allLocations.map((l, id) => [locationNetworkNameLookup[l.name], id] as const));
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

  let prevHintedLocations = List<Hint | null>(Repeat(null, defs.allLocations.length));
  const hintedLocations = computed(() => prevHintedLocations = prevHintedLocations.withMutations((hl) => {
    const { team: myTeam, slot: mySlot } = client.players.self;
    for (const hint of reactiveHints()) {
      if (hint.item.sender.slot === mySlot && hint.item.sender.team === myTeam) {
        hl.set(locationNetworkIdToLocation[hint.item.locationId], hint);
      }
    }
  }));

  const itemName = (i: AutopelagoItem) => {
    return slotData.lactose_intolerant
      ? i.lactoseIntolerantName
      : i.lactoseName;
  };
  const itemNetworkNameLookup = pkg.itemTable;
  const itemNetworkIdToItem: Partial<Record<number, number>> = { };
  for (const item of defs.progressionItemsByYamlKey.values()) {
    itemNetworkIdToItem[itemNetworkNameLookup[itemName(defs.allItems[item])]] = item;
  }

  let prevHintedItems = List<Hint | null>(Repeat(null, defs.allItems.length));
  const hintedItems = computed(() => prevHintedItems = prevHintedItems.withMutations((hi) => {
    const { team: myTeam, slot: mySlot } = client.players.self;
    for (const hint of reactiveHints()) {
      if (hint.item.id in itemNetworkIdToItem && hint.item.receiver.slot === mySlot && hint.item.receiver.team === myTeam) {
        hi.set(itemNetworkIdToItem[hint.item.id] ?? NaN, hint);
      }
    }
  }));

  return {
    connectScreenState,
    client,
    pkg,
    messageLog,
    playersWithStatus,
    slotData,
    hintedLocations,
    hintedItems,
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

function createReactiveHints(client: Client, destroyRef?: DestroyRef): Signal<List<Hint>> {
  const hints = signal(List<Hint>(client.items.hints));
  function onHint(hint: Hint) {
    hints.update(hints => hints.push(hint));
  }
  function onHintUpdated(hint: Hint) {
    hints.update(hints => hints.update(hints.findIndex(h => h.uniqueKey === hint.uniqueKey), hint, () => hint));
  }
  function onHints(newHints: readonly Hint[]) {
    hints.update(hints => hints.push(...newHints));
  }
  client.items.on('hintsInitialized', onHints);
  client.items.on('hintReceived', onHint);
  client.items.on('hintFound', onHint);
  client.items.on('hintUpdated', onHintUpdated);
  if (destroyRef) {
    destroyRef.onDestroy(() => {
      client.items.off('hintsInitialized', onHints);
      client.items.off('hintReceived', onHint);
      client.items.off('hintFound', onHint);
      client.items.off('hintUpdated', onHintUpdated);
    });
  }
  return hints.asReadonly();
}

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

export interface PlayerAndStatus {
  player: Player;
  isSelf: boolean;
  status: ClientStatus | null;
  statusRaw: JSONSerializable;
}

const validPlayerStatuses = new Set(Object.values(clientStatuses));
function isValidClientStatus(val: JSONSerializable): val is ClientStatus {
  return typeof val === 'number' && validPlayerStatuses.has(val as ClientStatus);
}

function createReactivePlayerList(client: Client): Signal<List<Readonly<PlayerAndStatus>>> {
  const allPlayers = client.players.teams[client.players.self.team];
  const playerList = signal(List<PlayerAndStatus>(allPlayers.map(player => ({
    player,
    isSelf: player.slot === client.players.self.slot,
    status: null,
    statusRaw: null,
  }))));
  const onClientStatusUpdated: DataChangeCallback = (key: string, value: JSONSerializable) => {
    const slot = Number(/^_read_client_status_\d+_(?<slot>\d+)$/.exec(key)?.groups?.['slot']);
    if (!slot) {
      return;
    }

    playerList.update(players => players.set(slot, {
      player: allPlayers[slot],
      isSelf: allPlayers[slot].slot === client.players.self.slot,
      status: isValidClientStatus(value) ? value : null,
      statusRaw: value,
    }));
  };

  // there's no way to unsubscribe our callbacks, so I think this is the only such "reactive" thing
  // that would stack up if you keep subscribing and then unsubscribing a single instance of Client.
  // not an immediate problem, but it's annoying enough that I took note (airbreather 2026-02-11).
  void client.storage.notify(allPlayers.map(player => `_read_client_status_${player.team.toString()}_${player.slot.toString()}`), onClientStatusUpdated);
  return playerList.asReadonly();
}
