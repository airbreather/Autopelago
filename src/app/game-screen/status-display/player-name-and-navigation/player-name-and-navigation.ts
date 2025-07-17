import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-player-name-and-navigation',
  imports: [
    RouterLink
  ],
  template: `
    <div>
      <div>PLAYER NAME</div>
      <div><button routerLink="/">Back to Main Menu</button></div>
    </div>
  `,
  styles: ``,
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class
export class PlayerNameAndNavigation {
}
