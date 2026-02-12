import { type ClientStatus, clientStatuses, type Player, type SayPacket } from '@airbreather/archipelago.js';
import { withResource } from '@angular-architects/ngrx-toolkit';
import { computed, effect, resource } from '@angular/core';

import { patchState, signalStore, withComputed, withHooks, withMethods, withState } from '@ngrx/signals';
import { List, Set as ImmutableSet } from 'immutable';
import {
  BAKED_DEFINITIONS_BY_VICTORY_LANDMARK,
  BAKED_DEFINITIONS_FULL,
  VICTORY_LOCATION_NAME_LOOKUP,
} from '../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../data/slot-data';
import { targetLocationEvidenceFromJSONSerializable } from '../game/target-location-evidence';
import { makePlayerToken } from '../utils/make-player-token';
import { toWeighted } from '../utils/weighted-sampler';
import { withCleverTimer } from './with-clever-timer';
import { withGameState } from './with-game-state';

const ONLINE_AND_NOT_GOALED_STATUSES = new Set<ClientStatus>([clientStatuses.connected, clientStatuses.ready, clientStatuses.playing]);
export const GameStore = signalStore(
  withCleverTimer(),
  withGameState(),
  withState({
    game: null as AutopelagoClientAndData | null,
    processedMessageCount: 0,
  }),
  withResource(({ game }) => ({
    playerToken: resource({
      defaultValue: null,
      params: () => ({ game: game() }),
      loader: async ({ params: { game } }) => {
        if (game === null) {
          return null;
        }

        const { playerIcon, playerColor } = game.connectScreenState;
        return await makePlayerToken(playerIcon, playerColor);
      },
    }),
  }), { errorHandling: 'native' }), // errors are unexpected in cases where ANYTHING can work.
  withMethods(store => ({
    init(game: AutopelagoClientAndData) {
      const { connectScreenState, client, pkg, slotData, storedData, locationIsProgression, locationIsTrap } = game;

      const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];

      const locationNameLookup = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey].locationNameLookup;
      const checkedLocations = client.room.checkedLocations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1);
      const defs = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
      if (defs.moonCommaThe !== null && checkedLocations.length >= (defs.allLocations.length - 1)) {
        checkedLocations.push(defs.moonCommaThe.location);
      }
      patchState(store, {
        ...storedData,
        game,
        lactoseIntolerant: slotData.lactose_intolerant,
        victoryLocationYamlKey,
        locationIsProgression,
        locationIsTrap,
        enabledBuffs: new Set(slotData.enabled_buffs),
        enabledTraps: new Set(slotData.enabled_traps),
        messagesForChangedTarget: toWeighted(slotData.msg_changed_target),
        messagesForEnterGoMode: toWeighted(slotData.msg_enter_go_mode),
        messagesForEnterBK: toWeighted(slotData.msg_enter_bk),
        messagesForRemindBK: toWeighted(slotData.msg_remind_bk),
        messagesForExitBK: toWeighted(slotData.msg_exit_bk),
        messagesForCompletedGoal: toWeighted(slotData.msg_completed_goal),
        hyperFocusLocation: 'hyperFocusLocation' in storedData ? storedData.hyperFocusLocation : null,
        auraDrivenLocations: List(storedData.auraDrivenLocations),
        userRequestedLocations: List(storedData.userRequestedLocations),
        receivedItems: List<number>(),
        checkedLocations: ImmutableSet(checkedLocations),
        previousTargetLocationEvidence: targetLocationEvidenceFromJSONSerializable(storedData.previousTargetLocationEvidence),
        outgoingAnimatableActions: List(),
      });
      const itemsJustReceived: number[] = [];
      for (const item of client.items.received) {
        const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
        if (typeof itemKey === 'number') {
          itemsJustReceived.push(itemKey);
        }
      }

      store.receiveItems(itemsJustReceived);
      client.items.on('itemsReceived', (items) => {
        const itemsJustReceived: number[] = [];
        for (const item of items) {
          const itemKey = BAKED_DEFINITIONS_FULL.itemNameLookup.get(item.name);
          if (typeof itemKey === 'number') {
            itemsJustReceived.push(itemKey);
          }
        }

        store.receiveItems(itemsJustReceived);
      });

      client.room.on('locationsChecked', (locations) => {
        patchState(store, ({ checkedLocations, outgoingAnimatableActions }) => {
          checkedLocations = checkedLocations.withMutations((c) => {
            outgoingAnimatableActions = outgoingAnimatableActions.withMutations((a) => {
              const newlyCheckedLocations: number[] = [];
              for (const serverLocationId of locations) {
                const location = locationNameLookup.get(pkg.reverseLocationTable[serverLocationId]) ?? -1;
                const sizeBefore = c.size;
                c.add(location);
                if (c.size > sizeBefore) {
                  newlyCheckedLocations.push(location);
                }
              }
              if (newlyCheckedLocations.length > 0) {
                a.push({ type: 'check-locations', locations: newlyCheckedLocations });
              }
            });
          });
          return {
            checkedLocations,
            outgoingAnimatableActions,
          };
        });
      });

      store.registerCallback(store.advance);
      store._initTimer({
        minDurationMilliseconds: connectScreenState.minTimeSeconds * 1000,
        maxDurationMilliseconds: connectScreenState.maxTimeSeconds * 1000,
      });
    },
  })),
  withComputed((store) => {
    const bestEffortRandomPlayers = computed(() => {
      const game = store.game();
      if (game === null) {
        return List<Player>();
      }

      const otherRealPlayers = game.playersWithStatus().filter(p => !p.isSelf && p.player.slot !== 0);
      if (otherRealPlayers.isEmpty()) {
        // solo game.
        return List([game.client.players.self]);
      }

      // "definitely online" as opposed to those who have goaled so we can't tell the difference.
      const definitelyOnlinePlayers = otherRealPlayers.filter(p => p.status !== null && ONLINE_AND_NOT_GOALED_STATUSES.has(p.status));
      return definitelyOnlinePlayers.isEmpty()
        ? otherRealPlayers.map(p => p.player)
        : definitelyOnlinePlayers.map(p => p.player);
    });
    function _messageTemplate(message: string) {
      if (!message.includes('{RANDOM_PLAYER}')) {
        return message;
      }

      const randomPlayers = bestEffortRandomPlayers();
      if (randomPlayers.isEmpty()) {
        // don't spend too long thinking about a good answer here. it's only possible EXTREMELY
        // early during initialization, and there shouldn't be any reason to call us during those
        // points anyway, so it really doesn't matter.
        return message;
      }

      return message.replaceAll('{RANDOM_PLAYER}', () => {
        const idx = Math.floor(Math.random() * randomPlayers.size);
        const otherPlayer = randomPlayers.get(idx);
        if (!otherPlayer) {
          throw new Error('list.size is inconsistent with list.get(i). this is a bug in immutable.js');
        }
        return otherPlayer.alias;
      });
    }
    function _wrapMessageTemplate<T extends unknown[]>(f: ((...args: T) => string) | null) {
      return f === null
        ? null
        : (...args: T) => _messageTemplate(f(...args));
    }
    return {
      sampleMessageFull: computed(() => {
        const sampleMessage = store.sampleMessage();
        return {
          forChangedTarget: _wrapMessageTemplate(sampleMessage.forChangedTarget),
          forEnterGoMode: _wrapMessageTemplate(sampleMessage.forEnterGoMode),
          forEnterBK: _wrapMessageTemplate(sampleMessage.forEnterBK),
          forRemindBK: _wrapMessageTemplate(sampleMessage.forRemindBK),
          forExitBK: _wrapMessageTemplate(sampleMessage.forExitBK),
          forCompletedGoal: _wrapMessageTemplate(sampleMessage.forCompletedGoal),
        } satisfies typeof sampleMessage;
      }),
    };
  }),
  withHooks({
    onInit(store) {
      effect(() => {
        const game = store.game();
        if (!game) {
          return;
        }

        const defs = store.defs();
        const outgoingCheckedLocations = store.outgoingCheckedLocations().filter(l => l !== defs.moonCommaThe?.location);
        if (outgoingCheckedLocations.size > 0) {
          const locationNameLookup = game.pkg.locationTable;
          game.client.check(...outgoingCheckedLocations.map(l => locationNameLookup[defs.allLocations[l].name]));
          patchState(store, { outgoingCheckedLocations: outgoingCheckedLocations.clear() });
        }
      });

      const goalEffect = effect(() => {
        const game = store.game();
        if (!game) {
          return;
        }

        const sampleMessage = store.sampleMessageFull().forCompletedGoal;
        if (sampleMessage === null) {
          return;
        }

        if (!store.hasCompletedGoal()) {
          return;
        }

        game.client.goal();
        const { sendChatMessages, forOneTimeEvents } = game.connectScreenState;
        patchState(store, ({ outgoingMessages }) => ({
          outgoingMessages: sendChatMessages && forOneTimeEvents
            ? outgoingMessages.push(sampleMessage(Math.random()))
            : outgoingMessages,
        }));

        goalEffect.destroy();
      });

      effect(() => {
        const game = store.game();
        if (!game) {
          return;
        }

        if (!store.canEventuallyAdvance()) {
          return;
        }

        const client = game.client;
        const players = client.players;
        let processedMessageCount = store.processedMessageCount();
        for (const message of game.messageLog().skip(processedMessageCount)) {
          store.processMessage(message, players);
          ++processedMessageCount;
        }

        patchState(store, ({ outgoingMessages }) => {
          if (outgoingMessages.size > 0) {
            client.socket.send(...outgoingMessages.map(msg => ({
              cmd: 'Say',
              text: msg,
            } satisfies SayPacket)));
          }

          return {
            outgoingMessages: outgoingMessages.clear(),
            processedMessageCount,
          };
        });
      });
      effect(() => {
        const game = store.game();
        if (game === null) {
          return;
        }

        const { allLocations } = store.defs();
        const sampleMessage = store.sampleMessageFull().forChangedTarget;
        if (sampleMessage === null) {
          return;
        }

        if (store.outgoingAuraDrivenLocations().size === 0 || !store.canEventuallyAdvance()) {
          return;
        }

        patchState(store, ({ outgoingAuraDrivenLocations, outgoingMessages }) => {
          if (outgoingAuraDrivenLocations.size === 0) {
            return { };
          }

          const { sendChatMessages, whenTargetChanges } = game.connectScreenState;
          if (!(sendChatMessages && whenTargetChanges)) {
            return {
              outgoingAuraDrivenLocations: outgoingAuraDrivenLocations.clear(),
            };
          }

          return {
            outgoingAuraDrivenLocations: outgoingAuraDrivenLocations.clear(),
            outgoingMessages: outgoingMessages.withMutations((messages) => {
              for (const loc of outgoingAuraDrivenLocations) {
                messages.push(sampleMessage(Math.random()).replaceAll('{LOCATION}', allLocations[loc].name));
              }
            }),
          };
        });
      });
      const reportGoMode = effect(() => {
        if (store.hasCompletedGoal()) {
          reportGoMode.destroy();
          return;
        }

        const game = store.game();
        if (game === null) {
          return;
        }

        const { sendChatMessages, forOneTimeEvents } = game.connectScreenState;
        if (!(sendChatMessages && forOneTimeEvents)) {
          return;
        }

        if (store.targetLocationReason() !== 'go-mode') {
          return;
        }

        const sampleMessage = store.sampleMessageFull().forEnterGoMode;
        if (sampleMessage === null) {
          return;
        }
        const message = sampleMessage(Math.random());
        patchState(store, ({ outgoingMessages }) => ({ outgoingMessages: outgoingMessages.push(message) }));
        reportGoMode.destroy();
      });
      let prevInterval: number | null = null;
      effect(() => {
        const game = store.game();
        if (game === null) {
          return;
        }

        const { forEnterBK, forRemindBK, forExitBK } = store.sampleMessageFull();
        if (forEnterBK === null || forRemindBK === null || forExitBK === null) {
          return;
        }

        const { sendChatMessages, whenBecomingBlocked, whenStillBlocked, whenStillBlockedIntervalMinutes, whenBecomingUnblocked } = game.connectScreenState;
        if (!sendChatMessages) {
          return;
        }

        const targetLocationReason = store.targetLocationReason();
        if (targetLocationReason === 'nowhere-useful-to-move' || (prevInterval !== null && targetLocationReason === 'startled')) {
          if (prevInterval === null) {
            if (whenBecomingBlocked) {
              patchState(store, ({ outgoingMessages }) => ({
                outgoingMessages: outgoingMessages.push(forEnterBK(Math.random())),
              }));
            }

            prevInterval = setInterval(() => {
              if (whenStillBlocked) {
                patchState(store, ({ outgoingMessages }) => ({
                  outgoingMessages: outgoingMessages.push(forRemindBK(Math.random())),
                }));
              }
            }, (whenStillBlockedIntervalMinutes >= 15 ? whenStillBlockedIntervalMinutes : 15) * 60000);
          }
        }
        else if (prevInterval !== null) {
          clearInterval(prevInterval);
          prevInterval = null;
          if (whenBecomingUnblocked && targetLocationReason !== 'user-requested') {
            patchState(store, ({ outgoingMessages }) => ({
              outgoingMessages: outgoingMessages.push(forExitBK(Math.random())),
            }));
          }
        }
      });
      const uwin = effect(() => {
        if (!store.allLocationsAreChecked()) {
          return;
        }

        if (store.victoryLocationYamlKey() === 'snakes_on_a_planet' && store.currentLocation() !== store.defs().moonCommaThe?.location) {
          return;
        }

        patchState(store, ({ outgoingAnimatableActions }) => ({
          outgoingAnimatableActions: outgoingAnimatableActions.push({ type: 'u-win' }),
        }));

        uwin.destroy();
      });
    },
  }),
);
