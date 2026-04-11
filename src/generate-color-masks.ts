import { TinyColor } from '@ctrl/tinycolor';
import sharp from 'sharp';

const imgMap = { player1: 'pack_rat', player2: 'player2', player4: 'player4' } as const;

const runIt = async (ratName: keyof typeof imgMap) => {
  const colorIndex = new Map<string, number>();
  const pixels: (readonly [number, number])[][] = [];
  const data = await sharp(`public/assets/images/players/${imgMap[ratName]}.webp`)
    .raw()
    .toBuffer();
  for (let y = 0; y < 16; y++) {
    const rowStart = y * 1024;
    for (let x = 0; x < 16; x++) {
      const colStart = rowStart + (x * 16);
      const d = data.subarray(colStart, colStart + 4);
      if (d[3] === 0) {
        continue;
      }
      if (d[3] !== 255) {
        throw new Error(`partially transparent pixel: ${x.toString()},${y.toString()}`);
      }
      const col = new TinyColor({ r: d[0], g: d[1], b: d[2] }).toHex();
      let ind = colorIndex.get(col);
      if (ind === undefined) {
        ind = pixels.length;
        colorIndex.set(col, ind);
        pixels.push([]);
      }
      pixels[ind].push([x, y]);
    }
  }
  const xs = pixels.map(p => p.flatMap(([x]) => x));
  const ys = pixels.map(p => p.flatMap(([_, y]) => y));
  return [[...colorIndex.keys()], [...xs, ...ys]];
};

const [a, b, c] = await Promise.all((['player1', 'player2', 'player4'] as const).map(runIt));
console.log(JSON.stringify({ a, b, c }));
