import { Component, input } from '@angular/core';
import type { LandmarkYamlKey } from '../../../../../data/locations';

@Component({
  selector: 'app-tooltip',
  imports: [],
  template: `
    <div class="outer">
      <p>This would be the tooltip for {{landmark()}}</p>
    </div>
  `,
  styles: `
    .outer {
      width: 300px;
    }
  `,
})
export class Tooltip {
  readonly landmark = input.required<LandmarkYamlKey | null>();
}
