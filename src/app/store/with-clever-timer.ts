import { withImmutableState } from '@angular-architects/ngrx-toolkit';
import { effect } from '@angular/core';
import { patchState, signalStoreFeature, withHooks, withMethods, withProps } from '@ngrx/signals';
import { List } from 'immutable';

interface PausableTimerBase {
  running: boolean;
  remainingDuration: number;
}

interface PausableTimerPaused extends PausableTimerBase {
  running: false;
  resume(): void;
}

interface PausableTimerRunning extends PausableTimerBase {
  running: true;
  pause(): void;
}

type PausableTimer = PausableTimerPaused | PausableTimerRunning;

interface CreatePausableTimerOptions {
  running: boolean;
  initialDuration: number;
  getNextDuration(this: void): number;
}

function createPausableTimer(options: CreatePausableTimerOptions): PausableTimer {
  const { running, initialDuration, getNextDuration } = options;

  let nextDeadline = Date.now() + initialDuration;
  let remainingDuration = NaN;
  let timeout = NaN;

  const sendNotificationIfNeeded = (now: number) => {
    if (now >= nextDeadline) {
      nextDeadline = now + (remainingDuration || getNextDuration());
    }
  };

  const setNextTimeout = (now: number) => {
    timeout = setTimeout(() => {
      sendNotificationIfNeeded(Date.now());
    }, nextDeadline - now);
  };

  const timer = {
    running,
    get remainingDuration() {
      return remainingDuration || (nextDeadline - Date.now());
    },
    pause: () => {
      const pauseTime = Date.now();
      if (!timer.running) {
        throw new Error('timer is not running');
      }

      clearTimeout(timeout);
      timeout = NaN;
      sendNotificationIfNeeded(pauseTime);
      remainingDuration = nextDeadline - pauseTime;
    },
    resume() {
      if (timer.running) {
        throw new Error('timer is already running');
      }

      timer.running = true;
      const resumeTime = Date.now();
      nextDeadline = resumeTime + remainingDuration;
      remainingDuration = NaN;
      setNextTimeout(resumeTime);
    },
  };
  return timer;
}

export const withCleverTimer = () => signalStoreFeature(
  withImmutableState({
    running: true,
    _timer: null as PausableTimer | null,
  }),
  withProps(() => ({
    _prevDuration: NaN,
    _callbacks: List<() => void>(),
  })),
  withMethods(store => ({
    workDoneSnapshot: () => {
      const timer = store._timer();
      if (!timer) {
        return 0;
      }

      return timer.remainingDuration / store._prevDuration;
    },
    registerCallback: (callback: () => void) => {
      store._callbacks = store._callbacks.push(callback);
    },
    unregisterCallback: (callback: () => void) => {
      store._callbacks = store._callbacks.filter(c => c !== callback);
    },
    pause: () => {
      const timer = store._timer();
      if (!timer) {
        throw new Error('call init() first');
      }

      patchState(store, { running: false });
    },
    resume: () => {
      const timer = store._timer();
      if (!timer) {
        throw new Error('call init() first');
      }

      patchState(store, { running: true });
    },
    togglePause: () => {
      const timer = store._timer();
      if (!timer) {
        throw new Error('call init() first');
      }

      patchState(store, ({ running }) => ({ running: !running }));
    },
  })),
  withMethods(store => ({
    _initTimer: ({ minDuration, maxDuration }: { minDuration: number; maxDuration: number }) => {
      if (store._timer()) {
        throw new Error('already initialized');
      }

      if (!(minDuration <= maxDuration)) {
        throw new Error('minDuration must be <= maxDuration');
      }

      const range = maxDuration - minDuration;
      const getNextDuration = () => {
        store._prevDuration = (minDuration + Math.floor(Math.random() * range));
        return store._prevDuration;
      };
      const timer = createPausableTimer({
        running: store.running(),
        initialDuration: getNextDuration(),
        getNextDuration: () => {
          for (const callback of store._callbacks) {
            callback();
          }

          return getNextDuration();
        },
      });
      patchState(store, { _timer: timer });
    },
  })),
  withHooks({
    onInit: (store) => {
      effect(() => {
        const timer = store._timer();
        if (!timer) {
          return;
        }

        if (store.running()) {
          if (!timer.running) {
            timer.resume();
          }
        }
        else {
          if (timer.running) {
            timer.pause();
          }
        }
      });
    },
  }),
);
