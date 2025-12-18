import type { Signal } from '@angular/core';
import type BitArray from '@bitarray/typedarray';
import type { Client, Item, JSONRecord, PackageMetadata } from 'archipelago.js';
import { List } from 'immutable';
import type { Message } from '../archipelago-client';
import type { ConnectScreenState } from '../connect-screen/connect-screen-state';
import type { TargetLocationEvidence } from '../game/target-location-evidence';
import type { ToJSONSerializable } from '../utils/types';
import type { AutopelagoBuff, AutopelagoTrap, VictoryLocationName } from './resolved-definitions';

export type AutopelagoUserCustomizableMessage = [string, number];

export interface AutopelagoClientAndData {
  connectScreenState: ConnectScreenState;
  client: Client;
  pkg: PackageMetadata;
  locationIsProgression: Readonly<BitArray>;
  locationIsTrap: Readonly<BitArray>;
  messageLog: Signal<List<Message>>;
  hintedLocations: Signal<List<Item | null>>;
  slotData: AutopelagoSlotData;
  storedData: AutopelagoStoredData;
  storedDataKey: string;
}

export interface AutopelagoSlotData extends JSONRecord {
  version_stamp: string;
  victory_location_name: VictoryLocationName;
  enabled_buffs: AutopelagoBuff[];
  enabled_traps: AutopelagoTrap[];
  msg_changed_target: AutopelagoUserCustomizableMessage[];
  msg_enter_go_mode: AutopelagoUserCustomizableMessage[];
  msg_enter_bk: AutopelagoUserCustomizableMessage[];
  msg_remind_bk: AutopelagoUserCustomizableMessage[];
  msg_exit_bk: AutopelagoUserCustomizableMessage[];
  msg_completed_goal: AutopelagoUserCustomizableMessage[];
  lactose_intolerant: boolean;
}

export interface UserRequestedLocation extends JSONRecord {
  location: number;
  userSlot: number;
}

export interface AutopelagoStoredData extends JSONRecord {
  foodFactor: number;
  luckFactor: number;
  energyFactor: number;
  styleFactor: number;
  distractionCounter: number;
  startledCounter: number;
  hasConfidence: boolean;
  mercyFactor: number;
  sluggishCarryover: boolean;
  processedReceivedItemCount: number;
  currentLocation: number;
  previousTargetLocationEvidence: ToJSONSerializable<TargetLocationEvidence>;
  auraDrivenLocations: number[];
  userRequestedLocations: UserRequestedLocation[];
  // things below this line are added after 0.11.5
  hyperFocusLocation?: number | null;
}
