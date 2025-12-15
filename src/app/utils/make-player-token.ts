import { type ColorInput, TinyColor } from '@ctrl/tinycolor';
import type { PlayerIcon } from '../connect-screen/connect-screen-state';
import { applyPixelColors, getPixelTones } from './color-helpers';

const PLAYER_TOKEN_PATHS = {
  1: '/assets/images/players/pack_rat.webp',
  2: '/assets/images/players/player2.webp',
  4: '/assets/images/players/player4.webp',
} as const satisfies Record<PlayerIcon, string>;

const PLAYER_TOKEN_LOADING = {
  1: getImage(1),
  2: getImage(2),
  4: getImage(4),
} as const satisfies Record<PlayerIcon, Promise<HTMLImageElement>>;

async function getImage(playerIcon: PlayerIcon) {
  const img = new Image();
  img.src = PLAYER_TOKEN_PATHS[playerIcon];
  await img.decode();
  return img;
}

export async function makePlayerToken(playerIcon: PlayerIcon, playerColor: ColorInput) {
  const cnv = new OffscreenCanvas(64, 64);
  const t2d = cnv.getContext('2d');
  if (t2d === null) {
    throw new Error('Failed to get 2d context');
  }

  const tones = getPixelTones(await PLAYER_TOKEN_LOADING[playerIcon], t2d);
  applyPixelColors(new TinyColor(playerColor), tones);
  t2d.putImageData(tones.data, 0, 0);
  return { data: tones.data, canvas: cnv };
}
