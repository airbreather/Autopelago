import { TinyColor } from '@ctrl/tinycolor/dist';
import type { Vec2 } from '../data/locations';

const SHIFT_H = 21 - 26;
const SHIFT_S = 0.42 - 0.32;
const SHIFT_V = 0.10 - 0.21;

export interface PixelTones {
  readonly light: readonly Vec2[];
  readonly dark: readonly Vec2[];
  readonly data: ImageData;
}

const LIGHT_COLOR = new TinyColor('#382E26', { format: 'hex3' }).toRgb();
const DARK_COLOR = new TinyColor('#1A130F', { format: 'hex3' }).toRgb();
export function getPixelTones(img: HTMLImageElement, ctx: CanvasRenderingContext2D) {
  ctx.clearRect(0, 0, 64, 64);
  ctx.drawImage(img, 0, 0);
  const light: Vec2[] = [];
  const dark: Vec2[] = [];
  for (let y = 0; y < 16; y++) {
    for (let x = 0; x < 16; x++) {
      const [r, g, b] = ctx.getImageData(x * 4, y * 4, 1, 1).data;
      if (r === LIGHT_COLOR.r && g === LIGHT_COLOR.g && b === LIGHT_COLOR.b) {
        light.push([x, y]);
      }
      else if (r === DARK_COLOR.r && g === DARK_COLOR.g && b === DARK_COLOR.b) {
        dark.push([x, y]);
      }
    }
  }
  return { light, dark, data: ctx.getImageData(0, 0, 64, 64) };
}

export function applyPixelColors(lightColor: TinyColor, tones: PixelTones) {
  const darkColor = toRatDark(lightColor);
  apply(lightColor, tones.light, tones.data);
  apply(darkColor, tones.dark, tones.data);
}

function apply(color: TinyColor, pixels: readonly Vec2[], dat: ImageData) {
  const { r, g, b } = color.toRgb();
  const rgb = [r, g, b] as const;
  for (const [x, y] of pixels) {
    for (let iy = 0; iy < 4; iy++) {
      const yy = (y * 4 + iy) * 256;
      for (let ix = 0; ix < 4; ix++) {
        dat.data.set(rgb, yy + (x * 4 + ix) * 4);
      }
    }
  }
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
