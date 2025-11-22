import type { ParamMap } from '@angular/router';
import type { SymmetricPropertiesOf, TypeAssert } from '../util';

const QUERY_PARAM_NAME_MAP = {
  host: 'h',
  port: 'p',
  slot: 's',
  password: 'w',
  minTimeSeconds: 't',
  maxTimeSeconds: 'T',
  enableTileAnimations: 'A',
  enableRatAnimations: 'a',
  sendChatMessages: 'c',
  whenTargetChanges: 'z',
  whenBecomingBlocked: 'b',
  whenStillBlocked: 'B',
  whenBecomingUnblocked: 'u',
  forOneTimeEvents: 'o',
  h: 'host',
  p: 'port',
  s: 'slot',
  w: 'password',
  t: 'minTimeSeconds',
  T: 'maxTimeSeconds',
  A: 'enableTileAnimations',
  a: 'enableRatAnimations',
  c: 'sendChatMessages',
  z: 'whenTargetChanges',
  b: 'whenBecomingBlocked',
  B: 'whenStillBlocked',
  u: 'whenBecomingUnblocked',
  o: 'forOneTimeEvents',
} as const;

// noinspection JSUnusedLocalSymbols
type _AssertAllPropsAreSymmetric = TypeAssert<
  SymmetricPropertiesOf<typeof QUERY_PARAM_NAME_MAP> extends typeof QUERY_PARAM_NAME_MAP ? true : false
>;

export type ConnectScreenQueryParams = {
  [K in keyof ConnectScreenState as typeof QUERY_PARAM_NAME_MAP[K]]: ConnectScreenState[K] extends boolean ? 0 | 1 : ConnectScreenState[K];
};

export interface ConnectScreenState {
  slot: string;
  host: string;
  port: number;
  password: string;
  minTimeSeconds: number;
  maxTimeSeconds: number;
  enableTileAnimations: boolean;
  enableRatAnimations: boolean;
  sendChatMessages: boolean;
  whenTargetChanges: boolean;
  whenBecomingBlocked: boolean;
  whenStillBlocked: boolean;
  whenBecomingUnblocked: boolean;
  forOneTimeEvents: boolean;
}

export const CONNECT_SCREEN_STATE_DEFAULTS: ConnectScreenState = {
  slot: '',
  host: 'archipelago.gg',
  port: 38281,
  password: '',
  minTimeSeconds: 20,
  maxTimeSeconds: 30,
  enableTileAnimations: true,
  enableRatAnimations: true,
  sendChatMessages: true,
  whenTargetChanges: true,
  whenBecomingBlocked: true,
  whenStillBlocked: false,
  whenBecomingUnblocked: true,
  forOneTimeEvents: true,
} as const;

export function queryParamsFromConnectScreenState(s: Readonly<ConnectScreenState>): ConnectScreenQueryParams {
  return {
    [QUERY_PARAM_NAME_MAP.host]: s.host,
    [QUERY_PARAM_NAME_MAP.port]: s.port,
    [QUERY_PARAM_NAME_MAP.slot]: s.slot,
    [QUERY_PARAM_NAME_MAP.password]: s.password,
    [QUERY_PARAM_NAME_MAP.minTimeSeconds]: s.minTimeSeconds,
    [QUERY_PARAM_NAME_MAP.maxTimeSeconds]: s.maxTimeSeconds,
    [QUERY_PARAM_NAME_MAP.enableTileAnimations]: s.enableTileAnimations ? 1 : 0,
    [QUERY_PARAM_NAME_MAP.enableRatAnimations]: s.enableRatAnimations ? 1 : 0,
    [QUERY_PARAM_NAME_MAP.sendChatMessages]: s.sendChatMessages ? 1 : 0,
    [QUERY_PARAM_NAME_MAP.whenTargetChanges]: s.whenTargetChanges ? 1 : 0,
    [QUERY_PARAM_NAME_MAP.whenBecomingBlocked]: s.whenBecomingBlocked ? 1 : 0,
    [QUERY_PARAM_NAME_MAP.whenStillBlocked]: s.whenStillBlocked ? 1 : 0,
    [QUERY_PARAM_NAME_MAP.whenBecomingUnblocked]: s.whenBecomingUnblocked ? 1 : 0,
    [QUERY_PARAM_NAME_MAP.forOneTimeEvents]: s.forOneTimeEvents ? 1 : 0,
  };
}

export function connectScreenStateFromQueryParams(qp: ParamMap): ConnectScreenState {
  const slot = qp.get(QUERY_PARAM_NAME_MAP.slot);
  const host = qp.get(QUERY_PARAM_NAME_MAP.host);
  const port = Number(qp.get(QUERY_PARAM_NAME_MAP.port));
  if (!(slot && host && port)) {
    throw new Error(`Missing required query params. host (${QUERY_PARAM_NAME_MAP.host}), port (${QUERY_PARAM_NAME_MAP.port}), and slot (${QUERY_PARAM_NAME_MAP.slot}) must be provided!`);
  }

  return {
    slot,
    host,
    port,
    password: qp.get(QUERY_PARAM_NAME_MAP.password) ?? '',
    minTimeSeconds: Number(qp.get(QUERY_PARAM_NAME_MAP.minTimeSeconds)) || CONNECT_SCREEN_STATE_DEFAULTS.minTimeSeconds,
    maxTimeSeconds: Number(qp.get(QUERY_PARAM_NAME_MAP.maxTimeSeconds)) || CONNECT_SCREEN_STATE_DEFAULTS.maxTimeSeconds,
    enableTileAnimations: readBoolean(qp, 'enableTileAnimations'),
    enableRatAnimations: readBoolean(qp, 'enableRatAnimations'),
    sendChatMessages: readBoolean(qp, 'sendChatMessages'),
    whenTargetChanges: readBoolean(qp, 'whenTargetChanges'),
    whenBecomingBlocked: readBoolean(qp, 'whenBecomingBlocked'),
    whenStillBlocked: readBoolean(qp, 'whenStillBlocked'),
    whenBecomingUnblocked: readBoolean(qp, 'whenBecomingUnblocked'),
    forOneTimeEvents: readBoolean(qp, 'forOneTimeEvents'),
  };
}

type BooleanKey = {
  [K in keyof ConnectScreenState]: ConnectScreenState[K] extends boolean ? K : never;
}[keyof ConnectScreenState];

function readBoolean(qp: ParamMap, key: BooleanKey): boolean {
  switch (qp.get(QUERY_PARAM_NAME_MAP[key])) {
    case '0':
      return false;

    case '1':
      return true;

    default:
      return CONNECT_SCREEN_STATE_DEFAULTS[key];
  }
}
