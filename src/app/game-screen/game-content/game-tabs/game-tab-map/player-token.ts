import { DestroyRef, inject, resource, type Signal, type WritableSignal } from '@angular/core';
import { List } from 'immutable';
import { DropShadowFilter } from 'pixi-filters';
import { Assets, Graphics, Sprite, Texture, Ticker } from 'pixi.js';
import type { Vec2 } from '../../../../data/locations';
import type { AutopelagoDefinitions } from '../../../../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';
import type { AnimatableAction } from '../../../../game/defining-state';

export interface WiggleOptimizationBox {
  neutralAngle: number;
  scaleX: number;
  _cycleTime: number;
  _playerToken: Sprite;
}

const ROTATION_SCALE = Math.PI / 3200;
const CYCLE = 1000;
const HALF_CYCLE = CYCLE / 2;
const QUARTER_CYCLE = CYCLE / 4;
function doRotation(this: WiggleOptimizationBox, t: Ticker) {
  this._cycleTime = (this._cycleTime + t.deltaMS) % CYCLE;
  this._playerToken.scale.x = this.scaleX;
  this._playerToken.rotation = this.neutralAngle + (Math.abs(this._cycleTime - HALF_CYCLE) - QUARTER_CYCLE) * ROTATION_SCALE;
}

export interface CreatePlayerTokenOptions {
  ticker: Ticker;
  wiggleOptimizationBox: WiggleOptimizationBox;
  game: Signal<AutopelagoClientAndData | null>;
  defs: Signal<AutopelagoDefinitions>;
  position: WritableSignal<Vec2>;
  consumeOutgoingAnimatableActions(): List<AnimatableAction>;
}

export interface PlayerTokenResult {
  sprite: Sprite;
  position: Signal<Vec2>;
  pathGfx: Graphics;
}

export const SCALE = 0.25;
export function createPlayerToken(options: CreatePlayerTokenOptions) {
  const { wiggleOptimizationBox, position } = options;
  const destroyRef = inject(DestroyRef);
  const playerTokenResource = resource({
    defaultValue: null,
    params: () => ({ game: options.game() }),
    loader: async ({ params: { game } }) => {
      if (game === null) {
        return null;
      }

      const texture = await Assets.load<Texture>('assets/images/players/pack_rat.webp');
      const playerToken = wiggleOptimizationBox._playerToken = new Sprite(texture);
      playerToken.position.set(2, 80);
      playerToken.scale.set(SCALE);
      playerToken.filters = [new DropShadowFilter({
        blur: 1,
        offset: { x: 6, y: 6 },
        color: 'black',
      })];
      playerToken.anchor.set(0.5);

      if (game.connectScreenState.enableRatAnimations) {
        options.ticker.add(doRotation, wiggleOptimizationBox);
        destroyRef.onDestroy(() => options.ticker.remove(doRotation, wiggleOptimizationBox));
      }

      const pathGfx = new Graphics();
      return { sprite: playerToken, position: position.asReadonly(), pathGfx } as PlayerTokenResult;
    },
  });
  return playerTokenResource.asReadonly();
}
