import type { Signal } from '@angular/core';
import { Ajv } from 'ajv';
import type { Client, JSONRecord } from 'archipelago.js';
import { List } from 'immutable';
import type { Message } from '../game/messages';
import type { AutopelagoBuff, AutopelagoTrap, VictoryLocationName } from './resolved-definitions';

export type AutopelagoUserCustomizableMessage = [string, number];

export interface AutopelagoClientAndData {
  client: Client;
  messageLog: Signal<List<Message>>;
  slotData: AutopelagoSlotData;
  storedData: AutopelagoStoredData;
  storedDataKey: string;
  packageChecksum: string | null;
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
  priorityPriorityLocations: number[];
  priorityLocations: number[];
}

const ajv = new Ajv();
export const validateAutopelagoStoredData = ajv.compile<AutopelagoStoredData>({
  type: 'object',
  properties: {
    foodFactor: { type: 'number' },
    luckFactor: { type: 'number' },
    energyFactor: { type: 'number' },
    styleFactor: { type: 'number' },
    distractionCounter: { type: 'number' },
    startledCounter: { type: 'number' },
    hasConfidence: { type: 'boolean' },
    mercyFactor: { type: 'number' },
    sluggishCarryover: { type: 'boolean' },
    processedReceivedItemCount: { type: 'number' },
    currentLocation: { type: 'number' },
    priorityPriorityLocations: { type: 'array', items: { type: 'number' } },
    priorityLocations: { type: 'array', items: { type: 'number' } },
  },
  required: [
    'foodFactor',
    'luckFactor',
    'energyFactor',
    'styleFactor',
    'distractionCounter',
    'startledCounter',
    'hasConfidence',
    'mercyFactor',
    'sluggishCarryover',
    'processedReceivedItemCount',
    'currentLocation',
    'priorityPriorityLocations',
    'priorityLocations',
  ],
} as const);
