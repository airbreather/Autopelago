import type { Client, Hint, JSONRecord, PackageMetadata } from '@airbreather/archipelago.js';
import type { Signal } from '@angular/core';
import type BitArray from '@bitarray/typedarray';
import type { List } from 'immutable';
import type { Message, PlayerAndStatus } from '../archipelago-client';
import type { ConnectScreenState } from '../connect-screen/connect-screen-state';
import type { TargetLocationEvidence } from '../game/target-location-evidence';
import type { ToJSONSerializable } from '../utils/types';
import type { AutopelagoAura, AutopelagoBuff, AutopelagoTrap, VictoryLocationName } from './resolved-definitions';

export type AutopelagoUserCustomizableMessage = [string, number];

export interface AutopelagoClientAndData {
  connectScreenState: ConnectScreenState;
  client: Client;
  pkg: PackageMetadata;
  locationIsProgression: Readonly<BitArray>;
  locationIsTrap: Readonly<BitArray>;
  messageLog: Signal<List<Message>>;
  playersWithStatus: Signal<List<Readonly<PlayerAndStatus>>>;
  hintedLocations: Signal<List<Hint | null>>;
  hintedItems: Signal<List<Hint | null>>;
  ratHints: Signal<List<Hint>>;
  slotData: AutopelagoSlotData;
  storedData: AutopelagoStoredData;
  storedDataKey: string;
}

export interface AutopelagoSlotDataV0 extends JSONRecord {
  version_stamp: '0.10.0'; // the first change came AFTER this stopped being observed.
  victory_location_name: VictoryLocationName;
  enabled_buffs: AutopelagoBuff[]; // obsolete in 1.0.0
  enabled_traps: AutopelagoTrap[]; // obsolete in 1.0.0
  msg_changed_target: AutopelagoUserCustomizableMessage[];
  msg_enter_go_mode: AutopelagoUserCustomizableMessage[];
  msg_enter_bk: AutopelagoUserCustomizableMessage[];
  msg_remind_bk: AutopelagoUserCustomizableMessage[];
  msg_exit_bk: AutopelagoUserCustomizableMessage[];
  msg_completed_goal: AutopelagoUserCustomizableMessage[];
  lactose_intolerant: boolean;
}

export interface AutopelagoSlotDataV1 extends JSONRecord {
  victory_location_name: VictoryLocationName;
  msg_changed_target: AutopelagoUserCustomizableMessage[];
  msg_enter_go_mode: AutopelagoUserCustomizableMessage[];
  msg_enter_bk: AutopelagoUserCustomizableMessage[];
  msg_remind_bk: AutopelagoUserCustomizableMessage[];
  msg_exit_bk: AutopelagoUserCustomizableMessage[];
  msg_completed_goal: AutopelagoUserCustomizableMessage[];
  lactose_intolerant: boolean;

  // added in 1.0.0 so that we only need to modify the APWorld side to change the list of items that
  // are only for flavor, which is especially helpful for those Easter Egg items.
  auras_by_item_id: ToJSONSerializable<Readonly<Record<number, readonly AutopelagoAura[]>>>;
  rat_counts_by_item_id: ToJSONSerializable<Readonly<Record<number, number>>>;

  // removed in 1.0.0, explicitly EXCLUDED here because JSONRecord is otherwise too permissive.
  // ironically, they actually ARE present in the object at runtime for the sake of compatibility in
  // the other direction - but the type system is more helpful if it stops us from using them.
  version_stamp?: never;
  enabled_buffs?: never;
  enabled_traps?: never;
}

export type AutopelagoSlotData =
  | AutopelagoSlotDataV0
  | AutopelagoSlotDataV1
  ;

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
