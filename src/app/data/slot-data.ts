import type { Client, JSONRecord } from 'archipelago.js';
import type { AutopelagoBuff, AutopelagoTrap, VictoryLocationName } from './resolved-definitions';

export type AutopelagoUserCustomizableMessage = [string, number];

export interface AutopelagoClientAndData {
  client: Client;
  slotData: AutopelagoSlotData;
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
