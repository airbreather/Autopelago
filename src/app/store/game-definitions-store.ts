import { httpResource } from '@angular/common/http';
import { computed } from '@angular/core';

import { signalStore, withComputed } from '@ngrx/signals';
import { withResource } from '@angular-architects/ngrx-toolkit';

import { parse as YAMLParse } from 'yaml';

import { resolveDefinitions } from '../data/resolved-definitions';
import { type AutopelagoDefinitionsYamlFile } from '../data/definitions-file';
import { type FillerRegionName, isFillerRegionName } from '../data/locations';

export const GameDefinitionsStore = signalStore(
  { providedIn: 'root' },
  withResource(() => ({
    _defsResource: httpResource.text(() => 'assets/AutopelagoDefinitions.yml'),
  })),
  withComputed(store => ({
    _defs: computed(() => {
      const file = store._defsResourceValue();
      return file ? YAMLParse(file) as unknown as AutopelagoDefinitionsYamlFile : null;
    }),
  })),
  withComputed(store => ({
    resolvedDefs: computed(() => {
      const defs = store._defs();
      if (!defs) {
        return null;
      }

      return resolveDefinitions(defs);
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
