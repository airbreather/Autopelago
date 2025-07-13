export interface Location {
  sprite_index: number;
  coords: readonly [x: number, y: number];
}

export const LOCATIONS = {
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
} as const;
