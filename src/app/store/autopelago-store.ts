import type { SayPacket } from '@airbreather/archipelago.js';
import { withResource } from '@angular-architects/ngrx-toolkit';
import { effect, resource } from '@angular/core';

import { patchState, signalStore, withHooks, withMethods, withState } from '@ngrx/signals';
import { List, Set as ImmutableSet } from 'immutable';
import { BAKED_DEFINITIONS_BY_VICTORY_LANDMARK, VICTORY_LOCATION_NAME_LOOKUP } from '../data/resolved-definitions';
import type { AutopelagoClientAndData } from '../data/slot-data';
import { targetLocationEvidenceFromJSONSerializable } from '../game/target-location-evidence';
import { makePlayerToken } from '../utils/make-player-token';
import { toWeighted } from '../utils/weighted-sampler';
import { withCleverTimer } from './with-clever-timer';
import { withGameState } from './with-game-state';

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
  })),
  withMethods(store => ({
    init(game: AutopelagoClientAndData) {
      const { connectScreenState, client, pkg, slotData, storedData, progressionItemLookup, locationIsProgression, locationIsTrap } = game;

      const victoryLocationYamlKey = VICTORY_LOCATION_NAME_LOOKUP[slotData.victory_location_name];

      const { allLocations, locationNameLookup, moonCommaThe } = BAKED_DEFINITIONS_BY_VICTORY_LANDMARK[victoryLocationYamlKey];
      const checkedLocations = client.room.checkedLocations.map(l => locationNameLookup.get(pkg.reverseLocationTable[l]) ?? -1);
      if (moonCommaThe !== null && checkedLocations.length >= (allLocations.length - 1)) {
        checkedLocations.push(moonCommaThe.location);
      }
      patchState(store, {
        ...storedData,
        game,
        lactoseIntolerant: slotData.lactose_intolerant,
        victoryLocationYamlKey,
        progressionItemLookup,
        locationIsProgression,
        locationIsTrap,
        aurasByItemId: slotData.auras_by_item_id,
        ratCountsByItemId: slotData.rat_counts_by_item_id,
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
      store.receiveItems(client.items.received.map(i => i.id));
      client.items.on('itemsReceived', (items) => {
        store.receiveItems(items.map(i => i.id));
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

        const sampleMessage = store.sampleMessage().forCompletedGoal;
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
        const sampleMessage = store.sampleMessage().forChangedTarget;
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

        const sampleMessage = store.sampleMessage().forEnterGoMode;
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

        const { forEnterBK, forRemindBK, forExitBK } = store.sampleMessage();
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
