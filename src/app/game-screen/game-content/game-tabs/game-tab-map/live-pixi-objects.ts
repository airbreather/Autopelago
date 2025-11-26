import { Ticker } from 'pixi.js';
import { GameStore } from '../../../../store/autopelago-store';
import { createFillerMarkers } from './filler-markers';
import { createLandmarkMarkers } from './landmark-markers';
import { createPlayerToken } from './player-token';

export function createLivePixiObjects(store: InstanceType<typeof GameStore>) {
  const playerTokenResource = createPlayerToken({
    ticker: Ticker.shared,
    game: store.game,
    defs: store.defs,
    currentLocation: store.currentLocation,
    consumeOutgoingMoves: store.consumeOutgoingMoves,
  });
  const landmarksResource = createLandmarkMarkers({
    game: store.game,
    defs: store.defs,
    victoryLocationYamlKey: store.victoryLocationYamlKey,
    regionIsLandmarkWithUnsatisfiedRequirement: store.regionIsLandmarkWithUnsatisfiedRequirement,
    checkedLocations: store.checkedLocations,
  });
  const fillerMarkersSignal = createFillerMarkers({
    victoryLocationYamlKey: store.victoryLocationYamlKey,
    checkedLocations: store.checkedLocations,
  });

  return {
    playerTokenResource,
    landmarksResource,
    fillerMarkersSignal,
  };
}
