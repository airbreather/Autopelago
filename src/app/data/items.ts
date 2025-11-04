import * as baked from './baked.json';
import type { VictoryLocationYamlKey } from './resolved-definitions';

export const PROGRESSION_ITEMS_UP_TO_CAPTURED_GOLDFISH = [
  'pizza_rat',
  'ninja_rat',
  'premium_can_of_prawn_food',
  'priceless_antique',
  'chef_rat',
  'pie_rat',
  'giant_novelty_scissors',
] as const;

export const PROGRESSION_ITEMS_UP_TO_SECRET_CACHE = [
  ...PROGRESSION_ITEMS_UP_TO_CAPTURED_GOLDFISH,
  'computer_rat',
  'blue_turtle_shell',
  'banana_peel',
  'masterful_longsword',
  'macguffin',
  'fake_mouse_ears',
  'legally_binding_contract',
  'lost_ctrl_key',
  'hammer_of_problem_solving',
  'childs_first_hand_axe',
  'acro_rat',
  'artificial_grass',
  'fifty_cents',
  'notorious_r_a_t',
  'gym_rat',
  'forklift_certification',
  'virtual_key',
] as const;

export const PROGRESSION_ITEMS_FULL = [
  ...PROGRESSION_ITEMS_UP_TO_SECRET_CACHE,
  'map_of_the_entire_internet',
  'turbo_encabulator',
  'ratstronaut',
  'energy_drink_that_is_pure_rocket_fuel',
  'pile_of_scrap_metal_in_the_shape_of_a_rocket_ship',
  'quantum_sugar_cube',
  'ziggu_rat',
  'pharaoh_not_anti_mummy_spray',
  'foreign_coin',
  'playing_with_fire_for_dummies',
  'constellation_prize',
  'free_vowel',
  'red_matador_cape',
  'lab_rat',
  'mongoose_in_a_combat_spacecraft',
  'asteroid_belt',
  'moon_shaped_like_a_butt',
] as const;

export const PROGRESSION_ITEMS_BY_VICTORY_LOCATION = {
  captured_goldfish: PROGRESSION_ITEMS_UP_TO_CAPTURED_GOLDFISH,
  secret_cache: PROGRESSION_ITEMS_UP_TO_SECRET_CACHE,
  snakes_on_a_planet: PROGRESSION_ITEMS_FULL,
} as const satisfies Record<VictoryLocationYamlKey, readonly (keyof (typeof baked.items) | keyof (typeof baked.items.rats))[]>;
