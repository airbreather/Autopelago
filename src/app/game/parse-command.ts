import type { Player, PlayersManager } from '@airbreather/archipelago.js';
import type { PlayerChatMessage, ServerChatMessage } from '../archipelago-client';

export function parseCommand(msg: PlayerChatMessage | ServerChatMessage, players: PlayersManager): Command | null {
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

  // if we got here, then the entire rest of the message after "@{SlotName}" is the command.
  const cmd = text.substring(taggedSlotOrAlias.length);
  const quotesMatcher = /^"*|"*$/g;
  if (/^go /i.exec(cmd) !== null) {
    return {
      type: 'go',
      locationName: cmd.substring('go '.length).replaceAll(quotesMatcher, ''),
    };
  }

  if (/^stop /i.exec(cmd) !== null) {
    return {
      type: 'stop',
      locationName: cmd.substring('stop '.length).replaceAll(quotesMatcher, ''),
    };
  }

  if (/^list\b/i.exec(cmd) !== null) {
    return {
      type: 'list',
    };
  }

  if (/^help\b/i.exec(cmd) !== null) {
    return {
      type: 'help',
      actualTag: taggedSlotOrAlias,
    };
  }

  return {
    type: 'unrecognized',
    actualTag: taggedSlotOrAlias,
  };
}

export type CommandType =
  | 'unrecognized'
  | 'help'
  | 'go'
  | 'stop'
  | 'list'
  ;

interface CommonCommand<T extends CommandType> {
  type: T;
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

export type Command =
  | UnrecognizedCommand
  | HelpCommand
  | GoCommand
  | StopCommand
  | ListCommand
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
