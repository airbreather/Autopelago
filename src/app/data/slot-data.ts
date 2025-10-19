import type { AutopelagoBuff, AutopelagoTrap } from './definitions-file';
import type { JSONRecord } from 'archipelago.js';

export type AutopelagoUserCustomizableMessage = [string, number];

export interface AutopelagoSlotData extends JSONRecord {
  version_stamp: string;
  victory_location_name: string;
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
