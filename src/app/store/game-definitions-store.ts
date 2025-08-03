import { httpResource } from '@angular/common/http';
import { computed, Injectable, signal } from '@angular/core';

import YAML from 'yaml';

import { resolveDefinitions } from '../data/resolved-definitions';
import { AutopelagoDefinitionsYamlFile } from '../data/definitions-file';
import { FillerRegionName, isFillerRegionName } from '../data/locations';

@Injectable({ providedIn: 'root' })
export class GameDefinitionsStoreService {
  readonly #lactoseIntolerant = signal(false);
  readonly #defsResource = httpResource.text(() => 'assets/AutopelagoDefinitions.yml');
  readonly #defs = computed(() => {
    const file = this.#defsResource.value();
    return file ? YAML.parse(file) as unknown as AutopelagoDefinitionsYamlFile : null;
  });

  readonly resolvedDefs = computed(() => {
    const defs = this.#defs();
    if (!defs) {
      return null;
    }

    const lactoseIntolerant = this.#lactoseIntolerant();
    return resolveDefinitions(defs, lactoseIntolerant);
  });

  readonly fillerCountsByRegion = computed((): Partial<Record<FillerRegionName, number>> | null => {
    const resolved = this.resolvedDefs();
    if (!resolved) {
      return null;
    }

    const o: Partial<Record<FillerRegionName, number>> = { };
    for (const r of resolved.allRegions) {
      if (isFillerRegionName(r.yamlKey) && 'locs' in r) {
        o[r.yamlKey] = r.locs.length;
      }
    }

    return o;
  });

  setLactoseIntolerant(lactoseIntolerant: boolean) {
    this.#lactoseIntolerant.set(lactoseIntolerant);
  }
}
