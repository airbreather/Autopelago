import { computed, inject } from '@angular/core';
import { signalStore, withState, withComputed, withProps } from '@ngrx/signals';
import { HttpClient } from '@angular/common/http';
import YAML from 'yaml';
import { AutopelagoDefinitionsYamlFile } from '../data/definitions-file';
import { rxResource } from '@angular/core/rxjs-interop';
import { retryWithExponentialBackoff, strictObjectEntries } from '../util';
import { FillerRegionName } from '../data/locations';

export const GameDataStore = signalStore(
  { providedIn: 'root' },
  withState({}),
  withProps(() => ({
    _httpClient: inject(HttpClient),
  })),
  withProps(store => ({
    _defsResource: rxResource<string | null, null>({
      defaultValue: null,
      stream: () => {
        return store._httpClient.get('/assets/AutopelagoDefinitions.yml', {
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
    fillerCountsByRegion: computed(() => {
      const d = store.defs();
      if (!d) {
        return null;
      }

      const result: Partial<Record<FillerRegionName, number>> = {};
      for (const [name, filler] of strictObjectEntries(d.regions.fillers)) {
        result[name] =
          (filler.unrandomized_items.filler ?? 0)
          + (filler.unrandomized_items.useful_nonprogression ?? 0)
          + (filler.unrandomized_items.key?.length ?? 0);
      }

      return result as Record<FillerRegionName, number>;
    }),
  })),
);
