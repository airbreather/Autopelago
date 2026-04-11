import { TinyColor } from '@ctrl/tinycolor';
import type { PlayerIcon } from '../connect-screen/connect-screen-state';
import type { Vec2 } from '../data/locations';
import * as colorMasksRaw from './color-masks.json';

type PlayerColorMask = readonly [readonly string[], readonly (readonly number[])[]];
const getColorMask = (img: PlayerIcon) => {
  const k: keyof typeof colorMasksRaw = img === 1 ? 'a' : img === 2 ? 'b' : 'c';
  return colorMasksRaw[k] as unknown as PlayerColorMask;
};

const SHIFT_H = 21 - 26;
const SHIFT_S = 0.42 - 0.32;
const SHIFT_V = 0.10 - 0.21;

export interface PixelTones {
  readonly ld: readonly (readonly [...Vec2, 0 | 1])[];
  readonly other: Readonly<Uint8ClampedArray>;
}

const EMPTY_DATA = new Uint8ClampedArray(64 * 64 * 4);
const LIGHT_COLOR = '382e26';
const DARK_COLOR = '1a130f';
const DARK_COLOR_OOPS = '251e1a';
export function getPixelTones(img: PlayerIcon): PixelTones {
  const other = new Uint8ClampedArray(EMPTY_DATA);
  const [colors, pixelArrays] = getColorMask(img);
  const ld: (readonly [...Vec2, 0 | 1])[] = [];
  for (let i = 0; i < colors.length; i++) {
    const color = new TinyColor(colors[i]);
    const xs = pixelArrays[i];
    const ys = pixelArrays[i + colors.length];
    if (color.equals(LIGHT_COLOR)) {
      ld.push(...xs.map((x, j) => [x, ys[j], 0] as const));
    }
    else if (color.equals(DARK_COLOR) || color.equals(DARK_COLOR_OOPS)) {
      ld.push(...xs.map((x, j) => [x, ys[j], 1] as const));
    }
    else {
      const { r, g, b } = color.toRgb();
      const p = [r, g, b, 255];
      for (let i = 0; i < xs.length; i++) {
        const x = xs[i];
        const y = ys[i];
        for (let iy = 0; iy < 4; iy++) {
          const yy = (y * 4 + iy) * 256;
          for (let ix = 0; ix < 4; ix++) {
            other.set(p, yy + (x * 4 + ix) * 4);
          }
        }
      }
    }
  }
  ld.sort((a, b) => (a[1] * 16 + a[0]) - (b[1] * 16 + b[0]));
  return { ld, other };
}

export function applyPixelColors(lightColor: TinyColor, tones: PixelTones) {
  const { r: lr, g: lg, b: lb } = lightColor.toRgb();
  const { r: dr, g: dg, b: db } = toRatDark(lightColor).toRgb();
  const ld = [[lr, lg, lb, 255], [dr, dg, db, 255]];
  const data = new Uint8ClampedArray(tones.other);
  for (const [x, y, i] of tones.ld) {
    for (let iy = 0; iy < 4; iy++) {
      const yy = (y * 4 + iy) * 256;
      for (let ix = 0; ix < 4; ix++) {
        data.set(ld[i], yy + (x * 4 + ix) * 4);
      }
    }
  }
  return data;
}

function toRatDark(lightColor: TinyColor) {
  let { h, s, v } = lightColor.toHsv();
  h += SHIFT_H;
  s += SHIFT_S;
  v += SHIFT_V;

  if (h >= 360) {
    h -= 360;
  }
  if (h < 0) {
    h += 360;
  }
  if (s > 1) {
    s = 1;
  }
  if (s < 0) {
    s = 0;
  }
  if (v > 1) {
    v = 1;
  }
  if (v < 0) {
    v = 0;
  }

  return new TinyColor({ h, s, v });
}
