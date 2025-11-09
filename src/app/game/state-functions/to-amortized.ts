import type { DefiningGameState } from '../defining-state';
import type { DerivedGameState } from '../derived-state';

export default (<PrngState>(gameState: DefiningGameState<PrngState>) => {
  return {
    ...gameState,
    _fixLint: undefined,
  };
}) satisfies <PrngState>(gameState: DefiningGameState<PrngState>) => DerivedGameState<PrngState>;
