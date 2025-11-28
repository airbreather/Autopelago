import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterOutlet } from '@angular/router';

import versionInfo from '../version-info.json';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet/>
  `,
  styles: [],
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class App {
  constructor() {
    inject(Title).setTitle(`Autopelago ${versionInfo.version} | A Game So Easy, It Plays Itself!`);
  }
}
