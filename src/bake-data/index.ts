import { createBuilder } from '@angular-devkit/architect';
import { type JsonObject } from '@angular-devkit/core';
import { xxh3 } from '@node-rs/xxhash';

import * as fsp from 'node:fs/promises';
import * as path from 'node:path';
import { parse as YAMLParse } from 'yaml';

interface Options extends JsonObject {
  'defs-file': string;
}

export default createBuilder<Options>(async (options, ctx) => {
  try {
    const data = await fsp.readFile(options['defs-file'], { encoding: 'utf8' });
    const dataHash = xxh3.xxh128(data).toString(16);
    const cachePath = path.join(ctx.workspaceRoot, '.angular', 'cache');
    const dataHashPath = path.join(cachePath, 'last-defs-hash');
    await fsp.mkdir(cachePath, { recursive: true });
    try {
      const h = await fsp.readFile(dataHashPath, { encoding: 'utf8' });
      if (h === dataHash) {
        return { success: true };
      }
    }
    catch {
      // no problem
    }
    const defs: unknown = YAMLParse(data);
    const bakedFilePath = path.join(ctx.workspaceRoot, 'src', 'app', 'data', 'baked.json');
    await fsp.writeFile(bakedFilePath, JSON.stringify(defs), { encoding: 'utf8' });
    await fsp.writeFile(dataHashPath, dataHash, { encoding: 'utf8' });
    return { success: true };
  }
  catch (e: unknown) {
    const error = e instanceof Error ? e.message : String(e);
    return {
      success: false,
      error,
    };
  }
});
