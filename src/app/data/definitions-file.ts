import { FillerRegionName, LandmarkName } from './locations';

export interface AutopelagoDefinitionsYamlFile {
  version_stamp: string;
  items: Readonly<YamlItems>;
  regions: Readonly<YamlRegions>;
}

export type AutopelagoAura =
  'well_fed'
  | 'upset_tummy'
  | 'lucky'
  | 'unlucky'
  | 'energized'
  | 'sluggish'
  | 'distracted'
  | 'stylish'
  | 'startled'
  | 'smart'
  | 'conspiratorial'
  | 'confident';

type YamlItemName = string | readonly [lactoseName: string, lactoseIntolerantName: string];
type YamlNonProgressionTypeName = 'useful_nonprogression' | 'trap' | 'filler';

interface YamlKeyedItem {
  name: YamlItemName;
  flavor_text?: string;
  auras_granted?: readonly AutopelagoAura[];
  rat_count?: number;
}

type YamlBulkItem = YamlKeyedItem | readonly [name: YamlItemName, aurasGranted: readonly AutopelagoAura[]];
type YamlGameSpecificItemGroup = Partial<Readonly<Record<string, readonly YamlBulkItem[]>>>;
type YamlBulkItemOrGameSpecificItemGroup = YamlBulkItem | { game_specific: YamlGameSpecificItemGroup };
type YamlItems =
  Partial<Omit<Record<string, Readonly<YamlKeyedItem>>, YamlNonProgressionTypeName | 'rats'>>
  & { rats: Partial<Record<string, Readonly<YamlKeyedItem>>> }
  & Record<YamlNonProgressionTypeName, readonly Readonly<YamlBulkItemOrGameSpecificItemGroup>[]>;

export type YamlRequirement =
  { rat_count: number }
  | { item: string }
  | { all: readonly YamlRequirement[] }
  | { any: readonly YamlRequirement[] }
  | { any_two: readonly YamlRequirement[] };

interface YamlLandmark {
  name: string;
  flavor_text: string;
  ability_check_dc: number;
  requires: Readonly<YamlRequirement>;
  exits?: readonly FillerRegionName[];
}

type YamlLandmarks = Record<LandmarkName, YamlLandmark>;

interface YamlUnrandomizedItems {
  key?: readonly (string | { readonly item: string; readonly count: number })[];
  filler?: number;
  useful_nonprogression?: number;
}

interface YamlFiller {
  name_template: `${string} #{n}`;
  ability_check_dc?: number;
  unrandomized_items: Readonly<YamlUnrandomizedItems>;
  exits: readonly LandmarkName[];
}

type YamlFillers = Record<FillerRegionName, YamlFiller>;

interface YamlRegions {
  landmarks: Readonly<YamlLandmarks>;
  fillers: Readonly<YamlFillers>;
}
