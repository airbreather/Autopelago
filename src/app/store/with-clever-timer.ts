import { withImmutableState } from '@angular-architects/ngrx-toolkit';
import { patchState, signalStoreFeature, withMethods, withProps } from '@ngrx/signals';

interface PausableTimerBase {
  running: boolean;
  registerCallback(this: void, callback: (this: void) => void): void;
  unregisterCallback(this: void, callback: (this: void) => void): void;
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
  getNextDuration(this: void): number;
}

function createPausableTimer(options: CreatePausableTimerOptions): PausableTimer {
  const { getNextDuration } = options;

  let remainingDurationIfPaused = getNextDuration();
  let nextDeadlineIfRunning = NaN;
  let timeoutIfRunning = NaN;

  const callbacks: (() => void)[] = [];
  const setNextTimeout = () => {
    const duration = remainingDurationIfPaused || getNextDuration();
    remainingDurationIfPaused = NaN;
    timeoutIfRunning = setTimeout(() => {
      for (const callback of callbacks) {
        callback();
      }

      setNextTimeout();
    }, duration);
    nextDeadlineIfRunning = Date.now() + duration;
  };
  const resume = () => {
    if (Number.isFinite(nextDeadlineIfRunning)) {
      return;
    }

    setNextTimeout();
  };

  const pause = () => {
    const pausedTime = Date.now();
    if (Number.isFinite(remainingDurationIfPaused)) {
      return;
    }

    clearTimeout(timeoutIfRunning);
    timeoutIfRunning = NaN;
    remainingDurationIfPaused = Math.max(0, nextDeadlineIfRunning - pausedTime);
    nextDeadlineIfRunning = NaN;
  };

  return {
    get running() {
      return Number.isFinite(nextDeadlineIfRunning);
    },
    pause,
    resume,
    registerCallback(callback: (this: void) => void) {
      callbacks.push(callback);
    },
    unregisterCallback(callback: (this: void) => void) {
      const idx = callbacks.indexOf(callback);
      if (idx > -1) {
        callbacks.splice(idx, 1);
      }
    },
  };
}

export function withCleverTimer() {
  return signalStoreFeature(
    withImmutableState({
      running: true,
      _timer: null as PausableTimer | null,
    }),
    withProps(() => ({
      _callbacksBeforeTimerRegistered: [] as (() => void)[],
    })),
    withMethods(store => ({
      registerCallback: (callback: () => void) => {
        const timer = store._timer();
        if (timer === null) {
          store._callbacksBeforeTimerRegistered.push(callback);
        }
        else {
          timer.registerCallback(callback);
        }
      },
      unregisterCallback: (callback: () => void) => {
        const timer = store._timer();
        if (timer === null) {
          const idx = store._callbacksBeforeTimerRegistered.indexOf(callback);
          if (idx > -1) {
            store._callbacksBeforeTimerRegistered.splice(idx, 1);
          }
        }
        else {
          timer.unregisterCallback(callback);
        }
      },
      pause: () => {
        const timer = store._timer();
        if (timer?.running) {
          timer.pause();
          patchState(store, { running: false });
        }
      },
      resume: () => {
        const timer = store._timer();
        if (timer?.running === false) {
          timer.resume();
          patchState(store, { running: true });
        }
      },
      togglePause: () => {
        const timer = store._timer();
        if (timer === null) {
          return;
        }

        if (timer.running) {
          timer.pause();
          patchState(store, { running: false });
        }
        else {
          timer.resume();
          patchState(store, { running: true });
        }
      },
    })),
    withMethods(store => ({
      _initTimer: ({ minDurationMilliseconds, maxDurationMilliseconds }: { minDurationMilliseconds: number; maxDurationMilliseconds: number }) => {
        if (store._timer()) {
          throw new Error('already initialized');
        }

        if (!(minDurationMilliseconds <= maxDurationMilliseconds)) {
          throw new Error('min must be <= max');
        }

        const range = maxDurationMilliseconds - minDurationMilliseconds;
        const timer = createPausableTimer({
          getNextDuration: () => minDurationMilliseconds + Math.floor(Math.random() * range),
        });
        for (const callback of store._callbacksBeforeTimerRegistered) {
          timer.registerCallback(callback);
        }
        if (store.running()) {
          if (timer.running) {
            timer.pause();
          }
          else {
            timer.resume();
          }
        }
        patchState(store, { _timer: timer });
      },
    })),
  );
}
