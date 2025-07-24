import { Component, DestroyRef, effect, inject, signal } from '@angular/core';

import { Application, Assets, Container, Sprite, Texture, Ticker } from 'pixi.js';

import { DropShadowFilter } from 'pixi-filters';

import { GameStore } from '../../../../store/autopelago-store';

const ROTATION_SCALE = Math.PI / 3200;
function doRotation(this: PlayerToken, t: Ticker) {
  this.cycleTime = (this.cycleTime + t.deltaMS) % 1000;
  this.playerTokenContainer.rotation = (Math.abs(this.cycleTime - 500) - 250) * ROTATION_SCALE;
}

@Component({
  selector: 'app-player-token',
  imports: [],
  template: '',
  styles: '',
})
export class PlayerToken {
  readonly playerTokenContainer = new Container();
  cycleTime = 0;

  constructor() {
    const initData = signal({
      texture: null as Texture | null,
      app: null as Application | null,
      root: null as Container | null,
    });
    void Assets.load<Texture>('assets/images/players/pack_rat.webp').then((texture) => {
      initData.update(d => ({ ...d, texture }));
    });

    this.playerTokenContainer.position.set(2, 80);
    this.playerTokenContainer.scale.set(0.25);
    this.playerTokenContainer.filters = [new DropShadowFilter({
      blur: 1,
      offset: { x: 6, y: 6 },
      color: 'black',
    })];
    effect(() => {
      const { texture, app, root } = initData();
      if (!(texture && app && root)) {
        return;
      }

      root.removeChild(this.playerTokenContainer);

      this.playerTokenContainer.removeChildren();
      const playerToken = new Sprite(texture);
      playerToken.anchor.set(0.5);
      this.playerTokenContainer.addChild(playerToken);

      root.addChild(this.playerTokenContainer);
    });

    const destroyRef = inject(DestroyRef);
    inject(GameStore).registerPlugin({
      destroyRef,
      afterInit(app, root) {
        initData.update(d => ({ ...d, app, root }));
      },
    });

    Ticker.shared.add(doRotation, this);
    destroyRef.onDestroy(() => Ticker.shared.remove(doRotation, this));
  }
}
