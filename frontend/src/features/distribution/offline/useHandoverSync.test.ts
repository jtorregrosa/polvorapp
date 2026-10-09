import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { SyncRun } from './sync';
import { useHandoverSync, useOnline } from './useHandoverSync';

const DAY = '00000000-0000-4000-8000-000000000801';
const ADMIN = '00000000-0000-4000-8000-0000000000a1';
const SYNCED: SyncRun = { status: 'synced', recorded: 1, conflicts: 0, pending: 0 };

function setOnline(online: boolean) {
  Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => online });
  window.dispatchEvent(new Event(online ? 'online' : 'offline'));
}

afterEach(() => {
  setOnline(true);
});

describe('useOnline', () => {
  it('follows the browser going offline and online', () => {
    const { result } = renderHook(() => useOnline());
    expect(result.current).toBe(true);

    act(() => {
      setOnline(false);
    });
    expect(result.current).toBe(false);

    act(() => {
      setOnline(true);
    });
    expect(result.current).toBe(true);
  });
});

describe('useHandoverSync (distribution spec: Handover sync and conflicts)', () => {
  it('syncs when the screen opens, and tells the screen to re-read the device', async () => {
    const run = vi.fn(() => Promise.resolve(SYNCED));
    const onSynced = vi.fn();

    const { result } = renderHook(() => useHandoverSync(DAY, ADMIN, onSynced, run));

    await waitFor(() => {
      expect(result.current.last).toEqual(SYNCED);
    });
    expect(run).toHaveBeenCalledWith(DAY, ADMIN);
    expect(onSynced).toHaveBeenCalled();
    expect(result.current.lastSyncedAt).toBeInstanceOf(Date);
  });

  it('waits offline and syncs as soon as connectivity returns', async () => {
    setOnline(false);
    const run = vi.fn(() => Promise.resolve(SYNCED));
    renderHook(() => useHandoverSync(DAY, ADMIN, vi.fn(), run));
    expect(run).not.toHaveBeenCalled();

    act(() => {
      setOnline(true);
    });

    await waitFor(() => {
      expect(run).toHaveBeenCalledTimes(1);
    });
  });

  it('does nothing without a signed-in owner', () => {
    const run = vi.fn(() => Promise.resolve(SYNCED));

    renderHook(() => useHandoverSync(DAY, undefined, vi.fn(), run));

    expect(run).not.toHaveBeenCalled();
  });

  it('syncs on demand, running again after a run in progress instead of doubling it', async () => {
    let finish: (run: SyncRun) => void = () => undefined;
    const run = vi
      .fn<() => Promise<SyncRun>>()
      .mockImplementationOnce(() => new Promise<SyncRun>((resolve) => (finish = resolve)))
      .mockResolvedValue(SYNCED);
    const { result } = renderHook(() => useHandoverSync(DAY, ADMIN, vi.fn(), run));
    await waitFor(() => {
      expect(result.current.running).toBe(true);
    });

    // A handover captured while the run had already read the queue.
    await act(async () => {
      await result.current.sync();
      await result.current.sync();
    });
    expect(run).toHaveBeenCalledTimes(1);

    act(() => {
      finish(SYNCED);
    });
    await waitFor(() => {
      expect(run).toHaveBeenCalledTimes(2);
    });
    await waitFor(() => {
      expect(result.current.running).toBe(false);
    });
    await act(async () => {
      await result.current.sync();
    });
    expect(run).toHaveBeenCalledTimes(3);
  });

  it('tries a run that timed out again later while the device says it is online', async () => {
    const timedOut: SyncRun = { status: 'offline', recorded: 0, conflicts: 0, pending: 1 };
    const run = vi.fn<() => Promise<SyncRun>>().mockResolvedValueOnce(timedOut).mockResolvedValue(SYNCED);
    const { result } = renderHook(() => useHandoverSync(DAY, ADMIN, vi.fn(), run, 20));

    await waitFor(() => {
      expect(result.current.last).toEqual(SYNCED);
    });
    expect(run).toHaveBeenCalledTimes(2);
  });

  it('tries again later while something is left to send', async () => {
    const busy: SyncRun = { status: 'busy', recorded: 0, conflicts: 0, pending: 2 };
    const run = vi.fn<() => Promise<SyncRun>>().mockResolvedValueOnce(busy).mockResolvedValue(SYNCED);

    const { result } = renderHook(() => useHandoverSync(DAY, ADMIN, vi.fn(), run, 20));

    await waitFor(() => {
      expect(result.current.last).toEqual(SYNCED);
    });
    expect(run).toHaveBeenCalledTimes(2);
  });

  it('does not retry a batch the server refused as a whole', async () => {
    const refused: SyncRun = {
      status: 'failed',
      code: 'distribution.captureNotPowder',
      recorded: 0,
      conflicts: 0,
      pending: 3,
    };
    const run = vi.fn<() => Promise<SyncRun>>().mockResolvedValue(refused);

    const { result } = renderHook(() => useHandoverSync(DAY, ADMIN, vi.fn(), run, 20));
    await waitFor(() => {
      expect(result.current.last).toEqual(refused);
    });
    await new Promise((resolve) => setTimeout(resolve, 80));

    expect(run).toHaveBeenCalledTimes(1);
  });

  it('tries a run that broke again later, keeping what was left', async () => {
    const run = vi
      .fn<() => Promise<SyncRun>>()
      .mockRejectedValueOnce(new Error('Synthetic storage failure'))
      .mockResolvedValue(SYNCED);

    const { result } = renderHook(() => useHandoverSync(DAY, ADMIN, vi.fn(), run, 20));

    await waitFor(() => {
      expect(result.current.last).toEqual(SYNCED);
    });
    expect(run).toHaveBeenCalledTimes(2);
  });

  it('syncs again for another day', async () => {
    const run = vi.fn(() => Promise.resolve(SYNCED));
    const { rerender } = renderHook(({ day }) => useHandoverSync(day, ADMIN, vi.fn(), run), {
      initialProps: { day: DAY },
    });
    await waitFor(() => {
      expect(run).toHaveBeenCalledTimes(1);
    });

    rerender({ day: '00000000-0000-4000-8000-000000000899' });

    await waitFor(() => {
      expect(run).toHaveBeenLastCalledWith('00000000-0000-4000-8000-000000000899', ADMIN);
    });
  });

  it('shows a run that broke as failed, without losing anything', async () => {
    const run = vi.fn<() => Promise<SyncRun>>().mockRejectedValue(new Error('Synthetic storage failure'));
    const onSynced = vi.fn();

    const { result } = renderHook(() => useHandoverSync(DAY, ADMIN, onSynced, run));

    await waitFor(() => {
      expect(result.current.last?.status).toBe('failed');
    });
    expect(result.current.running).toBe(false);
    expect(onSynced).toHaveBeenCalled();
  });
});
