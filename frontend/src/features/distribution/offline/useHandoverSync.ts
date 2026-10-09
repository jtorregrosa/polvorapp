import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { syncQueue, type SyncRun } from './sync';

/** How often a device with handovers left to send tries again while online. */
export const RETRY_INTERVAL_MS = 30_000;

/** Whether the browser says it is online, kept up to date with its `online` and `offline` events. */
export function useOnline(): boolean {
  const [online, setOnline] = useState(() => navigator.onLine);
  useEffect(() => {
    const up = () => {
      setOnline(true);
    };
    const down = () => {
      setOnline(false);
    };
    window.addEventListener('online', up);
    window.addEventListener('offline', down);
    return () => {
      window.removeEventListener('online', up);
      window.removeEventListener('offline', down);
    };
  }, []);
  return online;
}

export interface HandoverSyncState {
  /** A run is in progress. */
  running: boolean;
  /** How the last run ended, if any. */
  last: SyncRun | undefined;
  /** When a run last sent everything it could. */
  lastSyncedAt: Date | undefined;
  /**
   * Runs now (the "Sync now" action). A run already in progress is not doubled: another one follows
   * it, so a handover captured meanwhile is sent too.
   */
  sync: () => Promise<void>;
}

/**
 * Whether a run should be tried again on its own: a busy server or rate limit, a run that broke (no
 * code), and one that timed out while the device still says it is online. A batch the server refused
 * as a whole (e.g. not the powder day) waits for the Admin.
 */
const retriesLater = (run: SyncRun | undefined): boolean =>
  run?.status === 'busy' || run?.status === 'offline' || (run?.status === 'failed' && run.code === undefined);

/**
 * Keeps a day's captured handovers flowing to the server (distribution spec: Handover sync and
 * conflicts; design D4): runs when the screen opens, when connectivity returns, on demand, and every
 * 30 s while a run should be tried again. `onSynced` lets the screen re-read the device's store.
 */
export function useHandoverSync(
  distributionId: string,
  ownerUserId: string | undefined,
  onSynced: () => void,
  run: typeof syncQueue = syncQueue,
  retryIntervalMs: number = RETRY_INTERVAL_MS,
): HandoverSyncState {
  const online = useOnline();
  const [state, setState] = useState<Omit<HandoverSyncState, 'sync'>>({
    running: false,
    last: undefined,
    lastSyncedAt: undefined,
  });
  const inFlight = useRef(false);
  // Asked for while a run was in flight: that run may have read the queue before the new handover.
  const again = useRef(false);
  const latest = useRef({ distributionId, ownerUserId, onSynced, run });
  useLayoutEffect(() => {
    latest.current = { distributionId, ownerUserId, onSynced, run };
  });

  const runOnce = useCallback(async () => {
    const { distributionId: day, ownerUserId: owner, run: send } = latest.current;
    if (!owner) return;
    setState((current) => ({ ...current, running: true }));
    try {
      const result = await send(day, owner);
      setState((current) => ({
        running: false,
        last: result,
        lastSyncedAt: result.status === 'synced' ? new Date() : current.lastSyncedAt,
      }));
    } catch {
      // Not a refusal the run understands (e.g. the device's storage failed): shown as failed and
      // tried again later; the queue is left as it is.
      setState((current) => ({
        ...current,
        running: false,
        last: {
          status: 'failed',
          recorded: 0,
          conflicts: current.last?.conflicts ?? 0,
          pending: current.last?.pending ?? 0,
        },
      }));
    }
    try {
      latest.current.onSynced();
    } catch {
      // Re-reading the device is the screen's concern; a failure there must not break the next run.
    }
  }, []);

  const sync = useCallback(async () => {
    if (inFlight.current) {
      again.current = true;
      return;
    }
    inFlight.current = true;
    try {
      do {
        again.current = false;
        await runOnce();
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- set by a call made while awaiting
      } while (again.current);
    } finally {
      inFlight.current = false;
    }
  }, [runOnce]);

  // On open, for another day, and whenever connectivity comes back.
  useEffect(() => {
    if (online && ownerUserId) void sync();
  }, [online, ownerUserId, distributionId, sync]);

  // While online and a run should be tried again, try every 30 s.
  const tryAgain = retriesLater(state.last);
  useEffect(() => {
    if (!online || !tryAgain) return;
    const timer = window.setInterval(() => void sync(), retryIntervalMs);
    return () => {
      window.clearInterval(timer);
    };
  }, [online, tryAgain, sync, retryIntervalMs]);

  return { ...state, sync };
}
