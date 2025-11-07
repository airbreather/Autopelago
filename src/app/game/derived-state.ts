import type { DefiningGameState } from './defining-state';

export interface DerivedGameState<PrngState> extends DefiningGameState<PrngState> {
  // rules for inclusion in this interface:
  // 1. MUST be FULLY derived from a combination of global definitions and DefiningGameState.
  // 2. MUST have AT LEAST ONE consumer for which no clearly obvious good alternative exists.
  //    (this doesn't preclude there being other consumers who use it out of convenience).
  // 3. MUST be FULLY immutable.
  _fixLint: undefined;
}
