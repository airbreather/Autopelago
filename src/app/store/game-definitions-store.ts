import { httpResource } from '@angular/common/http';
import { computed } from '@angular/core';

import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';

import { parse as YAMLParse } from 'yaml';

import { resolveDefinitions } from '../data/resolved-definitions';
import { AutopelagoDefinitionsYamlFile } from '../data/definitions-file';
import { FillerRegionName, isFillerRegionName } from '../data/locations';

export const GameDefinitionsStore = signalStore(
  { providedIn: 'root' },
  withState({
    lactoseIntolerant: false,
  }),
  withMethods(store => ({
    updateLactoseIntolerant(lactoseIntolerant: boolean) {
      patchState(store, { lactoseIntolerant });
    },
  })),
  withProps(() => ({
    _defsResource: httpResource.text(() => 'assets/AutopelagoDefinitions.yml'),
  })),
  withComputed(store => ({
    _defs: computed(() => {
      const file = store._defsResource.value();
      return file ? YAMLParse(file) as unknown as AutopelagoDefinitionsYamlFile : null;
    }),
  })),
  withComputed(store => ({
    resolvedDefs: computed(() => {
      const defs = store._defs();
      if (!defs) {
        return null;
      }

      const lactoseIntolerant = store.lactoseIntolerant();
      return resolveDefinitions(defs, lactoseIntolerant);
    }),
  })),
  withComputed(store => ({
    fillerCountsByRegion: computed((): Partial<Record<FillerRegionName, number>> | null => {
      const resolved = store.resolvedDefs();
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
    }),
  })),
);
