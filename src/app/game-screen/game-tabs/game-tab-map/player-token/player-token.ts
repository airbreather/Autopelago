import { Component, DestroyRef, effect, inject, signal } from '@angular/core';

import { Application, Assets, Container, Sprite, Texture, Ticker } from 'pixi.js';

import { DropShadowFilter } from 'pixi-filters';

import { GameStore } from '../../../../store/autopelago-store';

@Component({
  selector: 'app-player-token',
  imports: [],
  template: '',
  styles: '',
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class PlayerToken {
  constructor() {
    const initData = signal({
      texture: null as Texture | null,
      app: null as Application | null,
      root: null as Container | null,
    });
    void Assets.load<Texture>('assets/images/players/pack_rat.webp').then((texture) => {
      initData.update(d => ({ ...d, texture }));
    });

    const playerTokenContainer = new Container();
    playerTokenContainer.position.set(2, 80);
    playerTokenContainer.scale.set(0.25);
    playerTokenContainer.filters = [new DropShadowFilter({
      blur: 1,
      offset: { x: 6, y: 6 },
      color: 'black',
    })];
    effect(() => {
      const { texture, app, root } = initData();
      if (!(texture && app && root)) {
        return;
      }

      root.removeChild(playerTokenContainer);

      playerTokenContainer.removeChildren();
      const playerToken = new Sprite(texture);
      playerToken.anchor.set(0.5);
      playerTokenContainer.addChild(playerToken);

      root.addChild(playerTokenContainer);
    });

    inject(GameStore).registerPlugin({
      destroyRef: inject(DestroyRef),
      afterInit(app, root) {
        initData.update(d => ({ ...d, app, root }));
      },
    });

    const ROTATION_SCALE = Math.PI / 3200;
    Ticker.shared.add(function (t) {
      this.cycleTime = (this.cycleTime + t.deltaMS) % 1000;
      playerTokenContainer.rotation = (Math.abs(this.cycleTime - 500) - 250) * ROTATION_SCALE;
    }, { cycleTime: 0 });
  }
}
