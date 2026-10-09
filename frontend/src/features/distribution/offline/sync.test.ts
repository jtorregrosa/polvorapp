import 'fake-indexeddb/auto';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiProblemError } from '@/api/http';
import type { HandoverSyncRequest, HandoverSyncResponse, SyncResult } from '@/api/generated/model';
import { CAPTURE_PACKAGE, CAPTURED, SERVER_HANDOVER } from '../test-data';
import { listQueue, putCaptured, readPackage, resetCaptureDbForTests, savePackage } from './store';
import { SYNC_BATCH, syncQueue, UNKNOWN_REFUSAL, type SendHandovers } from './sync';

const ADMIN = CAPTURED.ownerUserId;
const DAY = CAPTURED.distributionId;

afterEach(async () => {
  await resetCaptureDbForTests();
});

/** A server that answers each handover with the outcome the test picks (recorded by default). */
function server(
  outcome: (id: string) => Partial<SyncResult> = () => ({}),
): SendHandovers & { calls: HandoverSyncRequest[] } {
  const calls: HandoverSyncRequest[] = [];
  const send = vi.fn((_day: string, request: HandoverSyncRequest): Promise<HandoverSyncResponse> => {
    calls.push(request);
    return Promise.resolve({
      results: (request.handovers ?? []).map((item) => ({
        id: item.id ?? '',
        outcome: 'RECORDED',
        code: null,
        handover: { ...SERVER_HANDOVER, id: item.id ?? '', holderEntryId: item.holderEntryId ?? '' },
        existing: null,
        ...outcome(item.id ?? ''),
      })),
    });
  });
  return Object.assign(send, { calls });
}

async function capture(count: number, prefix = 'h'): Promise<void> {
  for (let i = 0; i < count; i++) {
    await putCaptured({
      ...CAPTURED,
      id: `${prefix}-${String(i).padStart(3, '0')}`,
      capturedAt: `2031-04-18T09:${String(i % 60).padStart(2, '0')}:00.000Z`,
    });
  }
}

describe('handover sync (distribution spec: Handover sync and conflicts)', () => {
  it(`sends pending handovers in batches of ${String(SYNC_BATCH)} and moves the recorded ones into the package`, async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date('2031-04-17T18:00:00Z'));
    await capture(150);
    const send = server();

    const run = await syncQueue(DAY, ADMIN, send);

    expect(send.calls.map((c) => c.handovers?.length)).toEqual([100, 50]);
    expect(run).toEqual({ status: 'synced', recorded: 150, conflicts: 0, pending: 0 });
    expect(await listQueue(DAY, ADMIN)).toEqual([]);
    expect((await readPackage(DAY, ADMIN))?.package.handovers).toHaveLength(150);
  });

  it('keeps a refused handover as a conflict with its reason, and a busy one pending', async () => {
    await capture(3);
    const send = server((id) =>
      id === 'h-001'
        ? {
            outcome: 'REFUSED',
            code: 'distribution.alreadyHandedOver',
            handover: null,
            existing: SERVER_HANDOVER,
          }
        : id === 'h-002'
          ? { outcome: 'REFUSED', code: 'distribution.busy', handover: null }
          : {},
    );

    const run = await syncQueue(DAY, ADMIN, send);

    const queue = await listQueue(DAY, ADMIN);
    expect(queue.map((h) => [h.id, h.state, h.code])).toEqual([
      ['h-001', 'conflict', 'distribution.alreadyHandedOver'],
      ['h-002', 'pending', null],
    ]);
    expect(queue[0]?.existing).toEqual(SERVER_HANDOVER);
    expect(run).toEqual({ status: 'busy', recorded: 1, conflicts: 1, pending: 1 });
  });

  it('keeps an edit made during the run for the next one', async () => {
    await capture(1);
    const recordOldVersion = server();
    const send: SendHandovers = async (day, request, signal) => {
      await putCaptured({ ...CAPTURED, id: 'h-000', rentalFlaskNumber: 'P-999' });
      return recordOldVersion(day, request, signal);
    };

    await syncQueue(DAY, ADMIN, send);

    expect((await listQueue(DAY, ADMIN)).map((h) => [h.id, h.state, h.rentalFlaskNumber])).toEqual([
      ['h-000', 'pending', 'P-999'],
    ]);
  });

  it('sends again a handover the server did not answer for, and keeps a refusal without a reason as a conflict', async () => {
    await capture(3);
    const send: SendHandovers = () =>
      Promise.resolve({
        results: [
          { id: 'h-000', outcome: 'REFUSED', code: null, handover: null, existing: null },
          { id: 'h-001', outcome: 'RECORDED', code: null, handover: null, existing: null },
        ],
      });

    const run = await syncQueue(DAY, ADMIN, send);

    const queue = await listQueue(DAY, ADMIN);
    expect(queue.map((h) => [h.id, h.state, h.code])).toEqual([
      ['h-000', 'conflict', UNKNOWN_REFUSAL],
      ['h-001', 'pending', null],
      ['h-002', 'pending', null],
    ]);
    expect(run).toEqual({ status: 'busy', recorded: 0, conflicts: 1, pending: 2 });
  });

  it('treats a request that timed out as lost connectivity', async () => {
    await capture(1);
    const send: SendHandovers = () => Promise.reject(new DOMException('Timed out', 'TimeoutError'));

    expect((await syncQueue(DAY, ADMIN, send)).status).toBe('offline');
  });

  it('never sends a conflict again until it is edited', async () => {
    await putCaptured({ ...CAPTURED, state: 'conflict', code: 'distribution.flaskNumberTaken' });
    const send = server();

    const run = await syncQueue(DAY, ADMIN, send);

    expect(send.calls).toEqual([]);
    expect(run).toEqual({ status: 'synced', recorded: 0, conflicts: 1, pending: 0 });
  });

  it('keeps the queue when the session has expired, to resume after signing in', async () => {
    await capture(2);
    const send: SendHandovers = () => Promise.reject(new ApiProblemError(401, undefined));

    const run = await syncQueue(DAY, ADMIN, send);

    expect(run.status).toBe('signedOut');
    expect(await listQueue(DAY, ADMIN)).toHaveLength(2);
  });

  it('keeps the queue when the network is unreachable', async () => {
    await capture(2);
    const send: SendHandovers = () => Promise.reject(new TypeError('Failed to fetch'));

    const run = await syncQueue(DAY, ADMIN, send);

    expect(run).toEqual({ status: 'offline', recorded: 0, conflicts: 0, pending: 2 });
  });

  it('stops at the rate limit and keeps the rest pending', async () => {
    await capture(2);
    const send: SendHandovers = () => Promise.reject(new ApiProblemError(429, undefined));

    expect((await syncQueue(DAY, ADMIN, send)).status).toBe('busy');
    expect(await listQueue(DAY, ADMIN)).toHaveLength(2);
  });

  it('reports any other refusal of the whole batch as failed, keeping the queue', async () => {
    await capture(1);
    const send: SendHandovers = () =>
      Promise.reject(new ApiProblemError(409, { code: 'distribution.captureNotPowder' }));

    expect(await syncQueue(DAY, ADMIN, send)).toEqual({
      status: 'failed',
      code: 'distribution.captureNotPowder',
      recorded: 0,
      conflicts: 0,
      pending: 1,
    });
  });

  it("sends only this Admin's handovers", async () => {
    await capture(1);
    await putCaptured({
      ...CAPTURED,
      id: 'someone-else',
      ownerUserId: '00000000-0000-4000-8000-0000000000a2',
    });
    const send = server();

    await syncQueue(DAY, ADMIN, send);

    expect(send.calls.flatMap((c) => c.handovers?.map((h) => h.id))).toEqual(['h-000']);
  });
});
