import { type DestroyRef, signal, type Signal } from '@angular/core';
import type { Client, MessageNode } from 'archipelago.js';
import { List } from 'immutable';

export interface Message {
  ts: Date;
  nodes: readonly MessageNode[];
}

export function createReactiveMessageLog(client: Client, destroyRef?: DestroyRef): Signal<List<Readonly<Message>>> {
  const messageLog = signal(List<Readonly<Message>>());
  const onMessage = (_text: string, nodes: readonly MessageNode[]) => {
    messageLog.update(messages => messages.push({
      ts: new Date(),
      nodes,
    }));
  };

  client.messages.on('message', onMessage);
  if (destroyRef) {
    destroyRef.onDestroy(() => {
      client.messages.off('message', onMessage);
    });
  }

  return messageLog.asReadonly();
}
