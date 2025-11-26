import { DestroyRef, inject, resource, type Signal } from '@angular/core';
import { List } from 'immutable';
import { DropShadowFilter } from 'pixi-filters';
import { Assets, Sprite, Texture, Ticker } from 'pixi.js';
import type { AutopelagoDefinitions } from '../../../../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../../../../data/slot-data';

const ROTATION_SCALE = Math.PI / 3200;
const CYCLE = 1000;
const HALF_CYCLE = CYCLE / 2;
const QUARTER_CYCLE = CYCLE / 4;

export interface WiggleOptimizationBox {
  neutralAngle: number;
  scaleX: number;
  _cycleTime: number;
  _playerToken: Sprite;
}

export interface CreatePlayerTokenOptions {
  ticker: Ticker;
  wiggleOptimizationBox: WiggleOptimizationBox;
  game: Signal<AutopelagoClientAndData | null>;
  defs: Signal<AutopelagoDefinitions>;
  currentLocation: Signal<number>;
  consumeOutgoingMoves(): List<readonly [number, number]>;
}

export const SCALE = 0.25;
export function createPlayerToken(options: CreatePlayerTokenOptions) {
  const { wiggleOptimizationBox } = options;
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
        function doRotation(this: WiggleOptimizationBox, t: Ticker) {
          this._cycleTime = (this._cycleTime + t.deltaMS) % CYCLE;
          this._playerToken.scale.x = this.scaleX;
          this._playerToken.rotation = this.neutralAngle + (Math.abs(this._cycleTime - HALF_CYCLE) - QUARTER_CYCLE) * ROTATION_SCALE;
        }

        options.ticker.add(doRotation, wiggleOptimizationBox);
        destroyRef.onDestroy(() => options.ticker.remove(doRotation, wiggleOptimizationBox));
      }

      return playerToken;
    },
  });
  return playerTokenResource.asReadonly();
}
