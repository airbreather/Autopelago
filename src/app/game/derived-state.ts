import type BitArray from '@bitarray/typedarray';
import { List } from 'immutable';
import type { AutopelagoDefinitions } from '../data/resolved-definitions';
import type { DefiningGameState } from './defining-state';

export interface DerivedGameState extends DefiningGameState {
  defs: Readonly<AutopelagoDefinitions>;
  ratCount: number;
  regionIsHardLocked: Readonly<BitArray>;
  regionIsSoftLocked: Readonly<BitArray>;
  locationIsChecked: Readonly<BitArray>;
  receivedItemCountLookup: List<number>;
}
