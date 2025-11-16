import type { Signal } from '@angular/core';
import type BitArray from '@bitarray/typedarray';
import type { Client, JSONRecord, PackageMetadata } from 'archipelago.js';
import { List } from 'immutable';
import type { Message } from '../archipelago-client';
import type { TargetLocationEvidence } from '../game/target-location-evidence';
import type { ConnectScreenStore } from '../store/connect-screen.store';
import type { ToJSONSerializable } from '../util';
import type {
  AutopelagoAura,
  AutopelagoBuff,
  AutopelagoItem,
  AutopelagoTrap,
  VictoryLocationName,
} from './resolved-definitions';

export type AutopelagoUserCustomizableMessage = [string, number];

export interface ResolvedAutopelagoItem extends AutopelagoItem {
  lactoseAwareName: string;
  enabledAurasGranted: readonly AutopelagoAura[];
}

export interface AutopelagoClientAndData {
  connectScreenStore: InstanceType<typeof ConnectScreenStore>;
  client: Client;
  pkg: PackageMetadata;
  resolvedItems: readonly ResolvedAutopelagoItem[];
  locationIsProgression: Readonly<BitArray>;
  locationIsTrap: Readonly<BitArray>;
  messageLog: Signal<List<Message>>;
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
  workDone: number;
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
}
