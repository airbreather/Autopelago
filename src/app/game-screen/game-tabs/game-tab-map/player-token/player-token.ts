import { Component, inject } from '@angular/core';
import { Assets, Sprite, Texture } from 'pixi.js';
import { DropShadowFilter } from 'pixi-filters';
import { PixiService } from '../pixi-service';

@Component({
  selector: 'app-player-token',
  imports: [],
  template: ``,
  styles: ``
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class PlayerToken {
  constructor() {
    let loadPlayerTokenTexture: Promise<Texture> | null = null;
    inject(PixiService).registerPlugin({
      beforeInit() {
        loadPlayerTokenTexture = Assets.load<Texture>('/assets/images/players/pack_rat.webp');
      },
      async afterInit(app, root) {
        if (!loadPlayerTokenTexture) {
          throw new Error('beforeInit() must finish before afterInit() may start');
        }

        const playerTokenTexture = await loadPlayerTokenTexture;
        const playerToken = new Sprite(playerTokenTexture);
        playerToken.anchor.set(0.5);
        playerToken.position.set(40, 40);
        playerToken.scale.set(0.25);
        playerToken.filters = [new DropShadowFilter({
          blur: 1,
          offset: { x: 6, y: 6 },
          color: 'black',
        })];
        root.addChild(playerToken);
        const ROTATION_SCALE = Math.PI / 3200;
        app.ticker.add(function (t) {
          this.cycleTime = (this.cycleTime + t.deltaMS) % 1000;
          playerToken.rotation = (Math.abs(this.cycleTime - 500) - 250) * ROTATION_SCALE;
        }, { cycleTime: 0 });
      }
    });
  }
}
