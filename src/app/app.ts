import { Component } from '@angular/core';

import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `
    <router-outlet/>
  `,
  styles: [],
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class App {
}
