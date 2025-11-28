import { Component } from '@angular/core';

import versionInfo from '../../../../../version-info.json';

@Component({
  selector: 'app-game-tab-app-build-info',
  imports: [],
  template: `
    <div class="outer">
      <span>Version:</span>
      <span>{{version}}</span>
      <span>Commit:</span>
      <span>{{commit}}</span>
      <span>Timestamp:</span>
      <span>{{timestamp}}</span>
    </div>
  `,
  styles: `
    .outer {
      display: grid;
      width: min-content;
      font-size: 30px;
      column-gap: 10px;
      grid-template-columns: auto auto;
      grid-auto-rows: auto;
    }
  `,
})
export class GameTabAppBuildInfo {
  protected version = versionInfo.version;
  protected commit = versionInfo.commit;
  protected timestamp = versionInfo.timestamp || new Date().toISOString();
}
