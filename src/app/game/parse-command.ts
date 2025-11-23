import type { Player, PlayersManager } from 'archipelago.js';
import type { Message } from '../archipelago-client';

export function parseCommand(msg: Message, players: PlayersManager): Command | null {
  const text = msg.nodes.join('').normalize();
  let taggedSlotOrAlias = `@${players.self.alias.normalize()} `;
  let tagIndex = text.indexOf(taggedSlotOrAlias);
  if (tagIndex < 0) {
    taggedSlotOrAlias = `@${players.self.name.normalize()} `;
    tagIndex = text.indexOf(taggedSlotOrAlias);
  }

  // chat message format is "{UserAlias}: {Message}", so it needs to be at least this long.
  if (tagIndex <= ': '.length) {
    return null;
  }

  const probablyPlayerAlias = text.substring(0, tagIndex - ': '.length);
  const requestingPlayer = findPlayerByAlias(players, probablyPlayerAlias);
  if (requestingPlayer === null && probablyPlayerAlias !== '[Server]') {
    // this isn't necessarily an error or a mistaken assumption. it could just be that the
    // '@${SlotName}' happened partway through their message. don't test every single user's
    // alias against every single chat message that contains '@${SlotName}', just require it
    // to be at the start of the message. done.
    return null;
  }

  const isAuthorized = requestingPlayer === null || requestingPlayer.team === players.self.team;

  // if we got here, then the entire rest of the message after "@{SlotName}" is the command.
  const cmd = text.substring(tagIndex + taggedSlotOrAlias.length);
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

  if (/^list /i.exec(cmd) !== null) {
    return {
      type: isAuthorized ? 'list' : 'list-unauthorized',
      requestingPlayer,
    };
  }

  if (/^help /i.exec(cmd) !== null) {
    return {
      type: 'help',
      requestingPlayer,
    };
  }

  return {
    type: 'unrecognized',
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

export type UnrecognizedCommand = CommonCommand<'unrecognized'>;
export type HelpCommand = CommonCommand<'help'>;

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
  return null;
}
