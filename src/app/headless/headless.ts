import { Component, computed, effect, inject, input } from '@angular/core';
import { AutopelagoService } from '../game/autopelago';
import type { LandmarkName } from '../data/locations';
import type { AutopelagoBuff, AutopelagoTrap } from '../data/definitions-file';
import type { ConnectedPacket } from 'archipelago.js';
import { GameDefinitionsStore } from '../store/game-definitions-store';
import BitArray from '@bitarray/typedarray';
import { strictObjectEntries } from '../util';
import { Subscription } from 'rxjs';
import type { AutopelagoDefinitions } from '../data/resolved-definitions';

type AutopelagoWeightedMessage = readonly [string, number];

interface AutopelagoSlotData {
  version_stamp: string;
  victory_location_name: LandmarkName;
  enabled_buffs: readonly AutopelagoBuff[];
  enabled_traps: readonly AutopelagoTrap[];
  msg_changed_target: readonly AutopelagoWeightedMessage[];
  msg_enter_go_mode: readonly AutopelagoWeightedMessage[];
  msg_enter_bk: readonly AutopelagoWeightedMessage[];
  msg_remind_bk: readonly AutopelagoWeightedMessage[];
  msg_exit_bk: readonly AutopelagoWeightedMessage[];
  msg_completed_goal: readonly AutopelagoWeightedMessage[];
  lactose_intolerant: boolean;
}

// noinspection JSUnusedLocalSymbols
type _AutopelagoConnectedPacket = ConnectedPacket & {
  readonly slot_data: Readonly<AutopelagoSlotData>;
};

interface GameState {
  foodFactor: number;
  luckFactor: number;
  energyFactor: number;
  styleFactor: number;
  distractionCounter: number;
  startledCounter: number;
  hasConfidence: boolean;
  mercyFactor: number;
  sluggishCarryover: boolean;
  receivedItems: number[];
  locationIsChecked: BitArray;
  itemByDataId: ReadonlyMap<number, number>;
  locationByDataId: ReadonlyMap<number, number>;
  dataIdByItem: readonly number[];
  dataIdByLocation: readonly number[];
  resolvedDefs: AutopelagoDefinitions;
}

@Component({
  selector: 'app-headless',
  imports: [],
  template: `
    <p>
      check your browser console, bro!
    </p>
  `,
  styles: '',
})
export class Headless {
  protected readonly autopelago = input.required<AutopelagoService>();

  constructor() {
    const gameDefinitionsStore = inject(GameDefinitionsStore);

    const gameState = computed<GameState | null>(() => {
      const res = gameDefinitionsStore.resolvedDefs();
      if (!res) {
        return null;
      }

      const dataPackage = this.autopelago().rawClient.dataPackage.value()?.games['Autopelago'];
      if (!dataPackage) {
        return null;
      }

      const itemByName = new Map(res.allItems.map((item, i) => [item.name, i]));
      const locationByName = new Map(res.allLocations.map((location, i) => [location.name, i]));

      const itemByDataId = new Map<number, number>();
      const dataIdByItem = new Array<number>(res.allItems.length);
      for (const [name, dataId] of strictObjectEntries(dataPackage.item_name_to_id)) {
        const item = itemByName.get(name);
        if (item === undefined) {
          // don't throw, this could just be lactose vs. intolerant
          continue;
        }

        itemByDataId.set(dataId, item);
        dataIdByItem[item] = dataId;
      }

      const locationByDataId = new Map<number, number>();
      const dataIdByLocation = new Array<number>(res.allLocations.length);
      for (const [name, dataId] of strictObjectEntries(dataPackage.location_name_to_id)) {
        const location = locationByName.get(name);
        if (location === undefined) {
          throw new Error(`did not recognize location ${name}`);
        }

        locationByDataId.set(dataId, location);
        dataIdByLocation[location] = dataId;
      }

      return {
        foodFactor: 0,
        luckFactor: 0,
        energyFactor: 0,
        styleFactor: 0,
        distractionCounter: 0,
        startledCounter: 0,
        hasConfidence: false,
        mercyFactor: 0,
        sluggishCarryover: false,
        receivedItems: new Array<number>(res.allItems.length).fill(0),
        locationIsChecked: new BitArray(res.allLocations.length),
        itemByDataId,
        locationByDataId,
        dataIdByItem,
        dataIdByLocation,
        resolvedDefs: res,
      };
    });

    let sub = new Subscription();
    effect(() => {
      sub.unsubscribe();
      const st = gameState();
      if (!st) {
        return;
      }

      sub = this.autopelago().rawClient.events('items', 'itemsReceived')
        .subscribe(([items]) => {
          for (const item of items) {
            const idx = st.itemByDataId.get(item.id);
            if (idx === undefined) {
              continue;
            }

            ++st.receivedItems[idx];
            for (const aura of st.resolvedDefs.allItems[idx].aurasGranted) {
              console.log('time for a', aura);
            }
          }
        });
    });
  }
}
