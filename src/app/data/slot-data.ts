import type { Signal } from '@angular/core';
import type { Client, JSONRecord } from 'archipelago.js';
import { List } from 'immutable';
import type { Message } from '../archipelago-client';
import type { PreviousLocationEvidence } from '../game/previous-location-evidence';
import type { ToJSONSerializable } from '../util';
import type { AutopelagoBuff, AutopelagoTrap, VictoryLocationName } from './resolved-definitions';

export type AutopelagoUserCustomizableMessage = [string, number];

export interface AutopelagoClientAndData {
  client: Client;
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
  previousLocationEvidence: ToJSONSerializable<PreviousLocationEvidence>;
  priorityPriorityLocations: number[];
  priorityLocations: number[];
}
