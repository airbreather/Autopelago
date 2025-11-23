import type { Player, PlayersManager } from 'archipelago.js';
import type { Message } from '../archipelago-client';

export function parseCommand(msg: Message, players: PlayersManager): Command | null {
  let requestingPlayer: Player | null = null;
  switch (msg.type) {
    case 'playerChat':
      requestingPlayer = msg.player;
      // eslint-disable-next-line no-fallthrough
    case 'serverChat': {
      if (!msg.text.startsWith('@')) {
        return null;
      }
      break;
    }
    default:
      return null;
  }

  const text = msg.text.normalize();
  let taggedSlotOrAlias = `@${players.self.alias.normalize()} `;
  if (!text.startsWith(taggedSlotOrAlias)) {
    taggedSlotOrAlias = `@${players.self.name.normalize()} `;
    if (!text.startsWith(taggedSlotOrAlias)) {
      if (players.self.alias === players.self.name) {
        return null;
      }

      // support "@NewAlias" instead of requiring it to be "@NewAlias (SlotName)", provided that no
      // other player's alias is also "@NewAlias".
      const simpleAliasMatch = /^(?<simpleAlias>.+) \(.*\)$/.exec(players.self.alias.normalize());
      if (simpleAliasMatch === null) {
        return null;
      }

      const simpleAlias = simpleAliasMatch.groups?.['simpleAlias'] ?? '';
      if (findPlayerByAlias(players, simpleAlias)?.slot !== players.self.slot) {
        return null;
      }

      taggedSlotOrAlias = `@${simpleAlias.normalize()} `;
      if (!text.startsWith(taggedSlotOrAlias)) {
        return null;
      }
    }
  }

  const isAuthorized = requestingPlayer === null || requestingPlayer.team === players.self.team;

  // if we got here, then the entire rest of the message after "@{SlotName}" is the command.
  const cmd = text.substring(taggedSlotOrAlias.length);
  const quotesMatcher = /^"*|"*$/g;
  if (/^go /i.exec(cmd) !== null) {
    return {
      type: isAuthorized ? 'go' : 'go-unauthorized',
      requestingPlayer,
      locationName: cmd.substring('go '.length).replaceAll(quotesMatcher, ''),
    };
  }

  if (/^stop /i.exec(cmd) !== null) {
    return {
      type: isAuthorized ? 'stop' : 'stop-unauthorized',
      requestingPlayer,
      locationName: cmd.substring('stop '.length).replaceAll(quotesMatcher, ''),
    };
  }

  if (/^list\b/i.exec(cmd) !== null) {
    return {
      type: isAuthorized ? 'list' : 'list-unauthorized',
      requestingPlayer,
    };
  }

  if (/^help\b/i.exec(cmd) !== null) {
    return {
      type: 'help',
      actualTag: taggedSlotOrAlias,
      requestingPlayer,
    };
  }

  return {
    type: 'unrecognized',
    actualTag: taggedSlotOrAlias,
    requestingPlayer,
  };
}

type RestrictedCommandType =
  | 'go'
  | 'stop'
  | 'list'
  ;

export type CommandType =
  | 'unrecognized'
  | 'help'
  | RestrictedCommandType
  | `${RestrictedCommandType}-unauthorized`
  ;

interface CommonCommand<T extends CommandType> {
  type: T;
  requestingPlayer: Player | null;
}
export interface UnrecognizedCommand extends CommonCommand<'unrecognized'> {
  actualTag: string;
}
export interface HelpCommand extends CommonCommand<'help'> {
  actualTag: string;
}
export interface GoCommand extends CommonCommand<'go'> {
  locationName: string;
}
export interface StopCommand extends CommonCommand<'stop'> {
  locationName: string;
}
export type ListCommand = CommonCommand<'list'>;
export type Unauthorized<T extends CommonCommand<RestrictedCommandType>> =
  Omit<T, 'type'> & { type: `${T['type']}-unauthorized` };

export type Command =
  | UnrecognizedCommand
  | HelpCommand
  | GoCommand
  | Unauthorized<GoCommand>
  | StopCommand
  | Unauthorized<StopCommand>
  | ListCommand
  | Unauthorized<ListCommand>
  ;

function findPlayerByAlias(players: PlayersManager, alias: string) {
  alias = alias.normalize();
  for (const t of players.teams) {
    for (const p of t) {
      if (p.alias.normalize() === alias) {
        return p;
      }
    }
  }

  // if we changed our alias to 'Foo', and there's NO OTHER PLAYER whose alias was also changed to
  // 'Foo', then it's safe to assume that '@Foo' was meant to refer to us.
  let found: Player | null = null;
  for (const t of players.teams) {
    for (const p of t) {
      if (p.alias.normalize() === `${alias} (${p.name.normalize()})`) {
        if (found !== null) {
          return null;
        }
        found = p;
      }
    }
  }
  return found === null || found.slot === 0
    ? null
    : found;
}
