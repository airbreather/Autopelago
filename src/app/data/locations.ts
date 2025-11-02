import { stricterObjectFromEntries, strictObjectEntries } from '../util';

import * as baked from './baked.json';

export type Vec2 = readonly [x: number, y: number];

export interface Landmark {
  sprite_index: number;
  coords: Vec2;
}

interface PreparedFiller {
  targetPoints: readonly Vec2[];
  targetPointsPrj: readonly number[];
}

export type LandmarkYamlKey = keyof typeof baked.regions.landmarks;
export const LANDMARKS = {
  basketball: { sprite_index: 1, coords: [59, 77] },
  prawn_stars: { sprite_index: 2, coords: [103, 34] },
  angry_turtles: { sprite_index: 3, coords: [103, 120] },
  pirate_bake_sale: { sprite_index: 4, coords: [166, 34] },
  restaurant: { sprite_index: 5, coords: [166, 120] },
  bowling_ball_door: { sprite_index: 6, coords: [254, 77] },
  captured_goldfish: { sprite_index: 7, coords: [290, 106] },
  computer_interface: { sprite_index: 8, coords: [282, 225] },
  kart_races: { sprite_index: 9, coords: [235, 179] },
  trapeze: { sprite_index: 10, coords: [235, 225] },
  daring_adventurer: { sprite_index: 11, coords: [235, 269] },
  broken_down_bus: { sprite_index: 12, coords: [178, 179] },
  blue_colored_screen_interface: { sprite_index: 13, coords: [178, 225] },
  overweight_boulder: { sprite_index: 14, coords: [178, 269] },
  binary_tree: { sprite_index: 15, coords: [124, 179] },
  copyright_mouse: { sprite_index: 16, coords: [124, 225] },
  computer_ram: { sprite_index: 17, coords: [124, 269] },
  rat_rap_battle: { sprite_index: 18, coords: [67, 179] },
  room_full_of_typewriters: { sprite_index: 19, coords: [67, 225] },
  stack_of_crates: { sprite_index: 20, coords: [67, 269] },
  secret_cache: { sprite_index: 21, coords: [20, 225] },
  makeshift_rocket_ship: { sprite_index: 22, coords: [25, 331] },
  roboclop_the_robot_war_horse: { sprite_index: 23, coords: [73, 353] },
  homeless_mummy: { sprite_index: 24, coords: [84, 402] },
  frozen_assets: { sprite_index: 25, coords: [54, 435] },
  alien_vending_machine: { sprite_index: 26, coords: [114, 428] },
  stalled_rocket_get_out_and_push: { sprite_index: 27, coords: [113, 334] },
  seal_of_fortune: { sprite_index: 28, coords: [149, 381] },
  space_opera: { sprite_index: 29, coords: [183, 346] },
  minotaur_labyrinth: { sprite_index: 30, coords: [194, 399] },
  asteroid_with_pants: { sprite_index: 31, coords: [232, 406] },
  snakes_on_a_planet: { sprite_index: 32, coords: [243, 354] },
} as const satisfies Readonly<Record<LandmarkYamlKey, Landmark>>;

export function isLandmarkYamlKey(yamlKey: string): yamlKey is LandmarkYamlKey {
  return yamlKey in LANDMARKS;
}

export interface Filler {
  coords: readonly Vec2[];
}

export type FillerRegionYamlKey = keyof typeof baked.regions.fillers;
export const FILLER_REGION_KEYS = [
  'Menu',
  'before_prawn_stars',
  'before_angry_turtles',
  'before_pirate_bake_sale',
  'before_restaurant',
  'after_pirate_bake_sale',
  'after_restaurant',
  'before_captured_goldfish',
  'before_computer_interface',
  'before_kart_races',
  'before_daring_adventurer',
  'before_broken_down_bus',
  'before_overweight_boulder',
  'before_copyright_mouse',
  'before_blue_colored_screen_interface',
  'before_room_full_of_typewriters',
  'before_trapeze',
  'before_binary_tree',
  'before_computer_ram',
  'before_rat_rap_battle',
  'before_stack_of_crates',
  'after_rat_rap_battle',
  'after_stack_of_crates',
  'before_makeshift_rocket_ship',
  'before_roboclop_the_robot_war_horse',
  'before_stalled_rocket_get_out_and_push',
  'before_homeless_mummy',
  'after_stalled_rocket_get_out_and_push',
  'before_frozen_assets',
  'before_alien_vending_machine',
  'after_homeless_mummy',
  'before_space_opera',
  'before_minotaur_labyrinth',
  'after_space_opera',
  'before_asteroid_with_pants',
  'after_minotaur_labyrinth',
] as const satisfies FillerRegionYamlKey[];

const FILLER_DEFINING_COORDS = {
  Menu: [[0, 77], [57, 77]],
  before_prawn_stars: [[61, 74], [90, 74], [90, 34], [101, 34]],
  before_angry_turtles: [[61, 80], [90, 80], [90, 120], [101, 120]],
  before_pirate_bake_sale: [[105, 34], [164, 34]],
  before_restaurant: [[105, 120], [164, 120]],
  after_pirate_bake_sale: [[168, 34], [183, 34], [185.44, 34.12], [189.16, 34.35], [192.08, 34.84], [194.46, 35.47], [197.73, 36.71], [200.45, 38.11], [204.34, 40.8], [206.22, 42.44], [208.74, 45.06], [210.48, 47.19], [212.72, 50.39], [213.99, 52.49], [215.71, 55.73], [217.51, 59.72], [219.29, 64.54], [220.79, 69.64], [221.69, 71.51], [222.2, 73.42], [223, 75], [252, 75]],
  after_restaurant: [[168, 120], [219, 120], [219, 105], [185, 105], [185, 91], [238, 91], [241, 80], [252, 80]],
  before_captured_goldfish: [[256, 77], [290, 77], [290, 104]],
  before_computer_interface: [[290, 108], [290, 225], [284, 225]],
  before_kart_races: [[281, 223], [237, 179]],
  before_daring_adventurer: [[280, 227], [237, 269]],
  before_broken_down_bus: [[233, 179], [180, 179]],
  before_overweight_boulder: [[233, 269], [180, 269]],
  before_copyright_mouse: [[176, 179], [167, 179], [124, 223]],
  before_blue_colored_screen_interface: [[178, 267], [178, 227]],
  before_room_full_of_typewriters: [[122, 225], [69, 225]],
  before_trapeze: [[180, 225], [233, 225]],
  before_binary_tree: [[124, 223], [124, 181]],
  before_computer_ram: [[177, 227], [135, 269], [126, 269]],
  before_rat_rap_battle: [[122, 179], [69, 179]],
  before_stack_of_crates: [[122, 269], [69, 269]],
  after_rat_rap_battle: [[65, 179], [21, 223]],
  after_stack_of_crates: [[65, 269], [62, 269], [20, 227]],
  before_makeshift_rocket_ship: [[18, 225], [6, 225], [6, 301], [6.6, 301.89], [6.6, 309.36], [7.25, 312.51], [7.88, 321.07], [8.1, 329.35], [8.78, 333.37], [10.39, 339.25], [12.12, 341.94], [15.63, 345.22], [19.91, 347.19], [28.01, 348.26], [34.65, 348.53], [39.42, 344.52], [41.18, 339.09], [41.39, 332.27], [39.81, 327.85], [35.69, 325.24], [32.55, 325.14], [28.95, 328.54], [26, 330]],
  before_roboclop_the_robot_war_horse: [[26, 330], [26.97, 331.42], [30.98, 331.87], [32.51, 332.98], [34.2, 336.21], [34.12, 340.35], [32.25, 343.41], [29.73, 344.24], [26.32, 344.07], [21.22, 342.68], [18.46, 341.34], [15.85, 338.21], [14.47, 335.2], [13.34, 329.66], [15.26, 324.43], [16.99, 321.45], [20.73, 317.9], [28.34, 315.93], [36.19, 316.15], [43.81, 318.42], [50.44, 324.81], [50.74, 326.38], [55.42, 335.44], [56.84, 338.87], [59.69, 344.3], [63.05, 347.94], [69.93, 351.34], [72, 353]],
  before_stalled_rocket_get_out_and_push: [[74, 352], [112, 335]],
  before_homeless_mummy: [[73, 354], [84, 401]],
  after_stalled_rocket_get_out_and_push: [[114, 333], [148, 380]],
  before_frozen_assets: [[83, 403], [55, 434]],
  before_alien_vending_machine: [[85, 403], [113, 427]],
  after_homeless_mummy: [[85, 402], [148, 382]],
  before_space_opera: [[149, 380], [156, 371], [157, 371], [174, 354], [175, 354], [182, 347]],
  before_minotaur_labyrinth: [[150, 382], [193, 399]],
  after_space_opera: [[184, 346], [242, 354]],
  before_asteroid_with_pants: [[195, 400], [231, 406]],
  after_minotaur_labyrinth: [[195, 398], [207, 386], [209, 385], [216, 378], [218, 377], [226, 369], [228, 368], [236, 360], [238, 359], [242, 355]],
} as const satisfies Readonly<Record<FillerRegionYamlKey, readonly Vec2[]>>;

export function isFillerRegionYamlKey(yamlKey: string): yamlKey is FillerRegionYamlKey {
  return yamlKey in FILLER_DEFINING_COORDS;
}

const PREPARED_FILLERS = stricterObjectFromEntries(
  strictObjectEntries(FILLER_DEFINING_COORDS)
    .map(([yamlKey, definingCoords]) => [yamlKey, convertDefiningPoints(yamlKey === 'Menu', definingCoords)]),
);

export function fillerRegionCoords(counts: Partial<Record<FillerRegionYamlKey, number>>): Readonly<Partial<Record<FillerRegionYamlKey, Filler>>> {
  const result: Partial<Record<FillerRegionYamlKey, Filler>> = {};
  for (const [yamlKey, { targetPoints, targetPointsPrj }] of strictObjectEntries(PREPARED_FILLERS)) {
    const transforms = [[0, 0], [0, 0], [0, 0], [0, 0]] as [number, number][];
    if (yamlKey === 'Menu') {
      transforms[0][1] = 3;
      transforms[2][1] = -3;
    }

    const count = counts[yamlKey];
    if (!count) {
      continue;
    }

    const coords: Vec2[] = [];
    for (let i = 0; i < count; i++) {
      coords.push(add(project((i / (count - 1)) * targetPointsPrj[targetPointsPrj.length - 1], targetPoints, targetPointsPrj), transforms[i & 3]));
    }

    result[yamlKey] = { coords };
  }

  return result;
}

function convertDefiningPoints(
  isStart: boolean,
  definingPointsOrig: readonly Vec2[],
): PreparedFiller {
  const DENSIFY_MULTIPLIER = 20;
  const densifiedLength = ((definingPointsOrig.length - 1) * DENSIFY_MULTIPLIER) + 1;
  const densifiedDefiningPoints: Vec2[] = Array<Vec2>(densifiedLength).fill([0, 0]);
  const densifiedTargetPoints: Vec2[] = Array<Vec2>(densifiedLength).fill([0, 0]);
  const densifiedDefiningPointsPrj = Array<number>(densifiedLength).fill(0);
  const densifiedTargetPointsPrj = Array<number>(densifiedLength).fill(0);

  densify(definingPointsOrig, densifiedDefiningPoints);

  // rename things so we don't need to have the word "densified" at all after this block.
  const definingPoints = densifiedDefiningPoints;
  const targetPoints = densifiedTargetPoints;
  const definingPointsPrj = densifiedDefiningPointsPrj;
  const targetPointsPrj = densifiedTargetPointsPrj;

  indexLine(definingPoints, definingPointsPrj);
  targetPointsPrj.splice(0, targetPointsPrj.length, ...definingPointsPrj);

  const originalLength = definingPointsPrj[definingPointsPrj.length - 1];
  const PADDING_AT_END = 8;
  if (isStart) {
    // start region needs less padding at the start
    const PADDING_AT_BEGINNING = 2;
    const newProportion =
      (originalLength - PADDING_AT_END - PADDING_AT_BEGINNING) / originalLength;
    for (let i = 0; i < targetPointsPrj.length; i++) {
      targetPointsPrj[i] = (targetPointsPrj[i] * newProportion) + PADDING_AT_BEGINNING;
    }
  }
  else {
    // all other filler regions need padding on both sides.
    const PADDING_AT_BEGINNING = 8;
    const newProportion =
      (originalLength - PADDING_AT_END - PADDING_AT_BEGINNING) / originalLength;
    for (let i = 0; i < targetPointsPrj.length; i++) {
      targetPointsPrj[i] = (targetPointsPrj[i] * newProportion) + PADDING_AT_BEGINNING;
    }
  }

  for (let i = 0; i < definingPoints.length; i++) {
    targetPoints[i] = project(targetPointsPrj[i], definingPoints, definingPointsPrj);
  }

  indexLine(targetPoints, targetPointsPrj);
  return { targetPoints, targetPointsPrj };
}

function densify(definingPoints: readonly Vec2[], densifiedPoints: Vec2[]) {
  const densifyMultiplier = (densifiedPoints.length - 1) / (definingPoints.length - 1);
  if (densifiedPoints.length !== ((definingPoints.length - 1) * densifyMultiplier) + 1) {
    throw new Error('Invalid densified points length');
  }
  densifiedPoints[0] = [...definingPoints[0]];
  for (let i = 1; i < definingPoints.length; i++) {
    const p0 = definingPoints[i - 1];
    const p1 = definingPoints[i];
    for (let j = 0; j < densifyMultiplier; j++) {
      const p1Share = (densifyMultiplier - j) / densifyMultiplier;
      densifiedPoints[(i * densifyMultiplier) - j] = add(mul(p0, 1 - p1Share), mul(p1, p1Share));
    }
  }
}

function distance(a: Vec2, b: Vec2) {
  return Math.sqrt(Math.pow(a[0] - b[0], 2) + Math.pow(a[1] - b[1], 2));
}

function indexLine(definingPoints: readonly Vec2[], endpointsPrj: number[]) {
  endpointsPrj[0] = 0;
  for (let i = 1; i < endpointsPrj.length; i++) {
    endpointsPrj[i] =
      endpointsPrj[i - 1] + distance(definingPoints[i - 1], definingPoints[i]);
  }
}

function project(prj: number, definingPoints: readonly Vec2[], definingPointsPrj: readonly number[]): Vec2 {
  // ASSUMPTION: prj <= definingPointsPrj.last()
  for (let i = 0; i < definingPointsPrj.length - 1; i++) {
    if (definingPointsPrj[i + 1] < prj) {
      continue;
    }

    const segPos = prj - definingPointsPrj[i];
    const segLen = definingPointsPrj[i + 1] - definingPointsPrj[i];
    const p1Share = segPos / segLen;
    return add(mul(definingPoints[i], 1 - p1Share), mul(definingPoints[i + 1], p1Share));
  }

  throw new Error('unreachable');
}

function add(a: Vec2, b: Vec2): Vec2 {
  return [a[0] + b[0], a[1] + b[1]];
}

function mul(a: Vec2, b: number): Vec2 {
  return [a[0] * b, a[1] * b];
}
