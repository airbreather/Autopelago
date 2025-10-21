import { type BuilderOutput, createBuilder } from '@angular-devkit/architect';
import { type JsonObject } from '@angular-devkit/core';

import { readFile } from 'node:fs/promises';

interface Options extends JsonObject {
  'defs-file': string;
}

export default createBuilder<Options>(async (options) => {
  const data = await readFile(options['defs-file'], { encoding: 'utf8' });
  return new Promise<BuilderOutput>((resolve) => {
    resolve({
      success: false,
      error: `This is where I would bake the data from ${options['defs-file']}.`,
    });
  });
});
