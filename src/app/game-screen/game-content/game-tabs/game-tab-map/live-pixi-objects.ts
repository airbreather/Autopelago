import { Sprite, type Ticker } from 'pixi.js';
import { GameStore } from '../../../../store/autopelago-store';
import { createFillerMarkers } from './filler-markers';
import { createLandmarkMarkers } from './landmark-markers';
import { createPlayerToken, SCALE, type WiggleOptimizationBox } from './player-token';

export function createLivePixiObjects(store: InstanceType<typeof GameStore>, ticker: Ticker) {
  const wiggleOptimizationBox: WiggleOptimizationBox = {
    neutralAngle: 0,
    scaleX: SCALE,
    _playerToken: new Sprite(), // will be overridden
    _cycleTime: 0, // will be overridden
  };
  const playerTokenResource = createPlayerToken({
    ticker,
    wiggleOptimizationBox,
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
