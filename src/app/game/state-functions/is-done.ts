import { BAKED_DEFINITIONS_BY_VICTORY_LANDMARK } from '../../data/resolved-definitions';
import type { DefiningGameState } from '../defining-state';

export default function (state: DefiningGameState) {
  return state.checkedLocations.size === BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[state.victoryLocationYamlKey].allLocations.length;
}
