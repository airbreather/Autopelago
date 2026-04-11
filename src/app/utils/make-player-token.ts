import { type ColorInput, TinyColor } from '@ctrl/tinycolor';
import type { PlayerIcon } from '../connect-screen/connect-screen-state';
import { applyPixelColors, getPixelTones } from './color-helpers';

export function makePlayerToken(playerIcon: PlayerIcon, playerColor: ColorInput) {
  const tones = getPixelTones(playerIcon);
  const cnv = new OffscreenCanvas(64, 64);
  const t2d = cnv.getContext('2d');
  if (t2d === null) {
    throw new Error('Failed to get 2d context');
  }

  const px = applyPixelColors(new TinyColor(playerColor), tones);
  const data = t2d.createImageData(64, 64);
  data.data.set(px);
  t2d.putImageData(data, 0, 0);
  return { data, canvas: cnv };
}
