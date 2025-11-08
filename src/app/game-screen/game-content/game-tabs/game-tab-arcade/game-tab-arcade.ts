import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-game-tab-arcade',
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div>
      congratulations, you found the arcade.
    </div>
  `,
  styles: '',
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class GameTabArcade {
}
