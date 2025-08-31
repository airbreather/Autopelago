import { DestroyRef, resource } from '@angular/core';

import { Assets, Sprite, Texture, Ticker } from 'pixi.js';

import { DropShadowFilter } from 'pixi-filters';

interface PlayerTokenContext {
  playerToken: Sprite;
  cycleTime: number;
}

const texturePromise = Assets.load<Texture>('assets/images/players/pack_rat.webp');

const ROTATION_SCALE = Math.PI / 3200;
const CYCLE = 1000;
const HALF_CYCLE = CYCLE / 2;
const QUARTER_CYCLE = CYCLE / 4;
function doRotation(this: PlayerTokenContext, t: Ticker) {
  this.cycleTime = (this.cycleTime + t.deltaMS) % CYCLE;
  this.playerToken.rotation = (Math.abs(this.cycleTime - HALF_CYCLE) - QUARTER_CYCLE) * ROTATION_SCALE;
}

export function createPlayerTokenTextureResource() {
  return resource({
    loader: () => texturePromise,
  }).asReadonly();
}

export function createPlayerToken(texture: Texture, destroyRef: DestroyRef) {
  const playerToken = new Sprite(texture);
  playerToken.position.set(2, 80);
  playerToken.scale.set(0.25);
  playerToken.filters = [new DropShadowFilter({
    blur: 1,
    offset: { x: 6, y: 6 },
    color: 'black',
  })];
  playerToken.anchor.set(0.5);

  const playerTokenContext: PlayerTokenContext = {
    playerToken,
    cycleTime: 0,
  };
  Ticker.shared.add(doRotation, playerTokenContext);
  destroyRef.onDestroy(() => Ticker.shared.remove(doRotation, playerTokenContext));
  return playerToken;
}
