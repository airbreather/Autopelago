import type { DerivedGameState } from './derived-state';

interface TurnStateBase extends DerivedGameState {
  readonly locationAttempts: number;
  readonly checkedAnyLocations: boolean;
  readonly confirmedTarget: boolean;
  readonly remainingActions: number;
  readonly energyBank: number;
  readonly targetLocation: number | null;
  readonly targetLocationReason: TargetLocationReason | null;
}

export type TargetLocationReason =
  | 'nowhere-useful-to-move'
  | 'closest-reachable-unchecked'
  | 'user-requested'
  | 'aura-driven'
  | 'go-mode'
  | 'startled'
  ;

export interface TurnStateBeforeConfirmingTargetLocation extends TurnStateBase {
  confirmedTarget: false;
  targetLocation: null;
  targetLocationReason: null;
}

export interface TurnStateWithConfirmedTargetLocation extends TurnStateBase {
  confirmedTarget: true;
  targetLocation: number;
  targetLocationReason: TargetLocationReason;
}

export type TurnState =
  | TurnStateBeforeConfirmingTargetLocation
  | TurnStateWithConfirmedTargetLocation;
