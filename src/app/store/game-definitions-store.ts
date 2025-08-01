import { HttpClient } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';

import { signalStore, withComputed, withProps, withState } from '@ngrx/signals';
import YAML from 'yaml';

import { AutopelagoDefinitionsYamlFile } from '../data/definitions-file';
import { resolveDefinitions } from '../data/resolved-definitions';
import { retryWithExponentialBackoff } from '../util';
import { FillerRegionName, isFillerRegionName } from '../data/locations';

export const GameDefinitionsStore = signalStore(
  { providedIn: 'root' },
  withState({
    lactoseIntolerant: false,
  }),
  withProps(() => ({
    _httpClient: inject(HttpClient),
  })),
  withProps(store => ({
    _defsResource: rxResource<string | null, null>({
      defaultValue: null,
      stream: () => {
        return store._httpClient.get('assets/AutopelagoDefinitions.yml', {
          responseType: 'text',
        }).pipe(retryWithExponentialBackoff());
      },
    }),
  })),
  withComputed(store => ({
    defs: computed(() => {
      const d = store._defsResource.value();
      return d
        ? YAML.parse(d) as AutopelagoDefinitionsYamlFile
        : null;
    }),
  })),
  withComputed(store => ({
    resolvedDefs: computed(() => {
      const d = store.defs();
      return d ? resolveDefinitions(d, store.lactoseIntolerant()) : null;
    }),
  })),
  withComputed(store => ({
    fillerCountsByRegion: computed(() => {
      const d = store.resolvedDefs();
      if (!d) {
        return null;
      }

      const o: Partial<Record<FillerRegionName, number>> = { };
      for (const r of d.allRegions) {
        if (isFillerRegionName(r.yamlKey) && 'locs' in r) {
          o[r.yamlKey] = r.locs.length;
        }
      }

      return o;
    }),
  })),
);
