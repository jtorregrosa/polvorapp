import { syncHandovers } from '@/api/generated/distribution/distribution';
import type { HandoverSyncRequest, HandoverSyncResponse } from '@/api/generated/model';
import { ApiProblemError, responseData } from '@/api/http';
import { applySynced, listQueue, markConflict, type CapturedHandover } from './store';

/** The server takes at most 100 handovers per request (spec: Handover sync and conflicts). */
export const SYNC_BATCH = 100;

/** A request that takes longer is treated as lost connectivity; the batch is sent again later. */
export const SYNC_TIMEOUT_MS = 30_000;

/** A refusal that only means "later": the device keeps the handover pending. */
const RETRY_LATER = 'distribution.busy';

/** A refusal the server gave no reason for: kept as a conflict, never sent again on its own. */
export const UNKNOWN_REFUSAL = 'distribution.unknown';

/** Sends one batch of a day's handovers; injectable for tests. */
export type SendHandovers = (
  distributionId: string,
  request: HandoverSyncRequest,
  signal: AbortSignal,
) => Promise<HandoverSyncResponse>;

/**
 * How a sync run ended: everything sent (`synced`), stopped without connectivity, with an expired
 * session, at the rate limit or a busy server, or refused as a whole (`failed`, with the refusal's
 * code; a run that broke has none); with what is left.
 */
export interface SyncRun {
  status: 'synced' | 'offline' | 'signedOut' | 'busy' | 'failed';
  /** The batch's refusal code, for a `failed` run the server refused. */
  code?: string;
  recorded: number;
  conflicts: number;
  pending: number;
}

const sendWithApi: SendHandovers = async (distributionId, request, signal) =>
  responseData(await syncHandovers(distributionId, request, { signal }));

const toItem = (handover: CapturedHandover) => ({
  id: handover.id,
  holderEntryId: handover.holderEntryId,
  distributionNumber: handover.distributionNumber,
  collectedBy: handover.collectedBy,
  collectorEntryId: handover.collectorEntryId,
  rentalFlaskNumber: handover.rentalFlaskNumber,
  traceability1: handover.traceability1,
  traceability2: handover.traceability2,
  collectedAt: handover.collectedAt,
});

/** Why the request for a batch did not come back; anything else is a bug and propagates. */
function stopped(error: unknown): Pick<SyncRun, 'status' | 'code'> {
  if (error instanceof ApiProblemError) {
    if (error.status === 401) return { status: 'signedOut' };
    if (error.status === 429 || error.status === 503) return { status: 'busy' };
    return { status: 'failed', ...(error.problem?.code ? { code: error.problem.code } : {}) };
  }
  // fetch rejects with a TypeError when the network cannot be reached, and the timeout aborts it.
  if (
    error instanceof TypeError ||
    (error instanceof DOMException && ['TimeoutError', 'AbortError'].includes(error.name))
  ) {
    return { status: 'offline' };
  }
  throw error;
}

/**
 * Sends this Admin's pending handovers of the day in batches (add-offline-distribution-capture D4):
 * recorded ones move into the day's package, refused ones stay as conflicts with their reason, and
 * busy ones, or any the server did not answer for, stay pending. Each outcome settles only the
 * version sent, so an edit made during the run is sent next time. Conflicts are never sent again
 * until the Admin edits them. An expired session, no connectivity or the rate limit stop the run
 * with nothing lost.
 */
export async function syncQueue(
  distributionId: string,
  ownerUserId: string,
  send: SendHandovers = sendWithApi,
): Promise<SyncRun> {
  const queue = await listQueue(distributionId, ownerUserId);
  const pending = queue.filter((h) => h.state === 'pending');
  let recorded = 0;
  let conflicts = queue.length - pending.length;
  let retry = 0;

  for (let start = 0; start < pending.length; start += SYNC_BATCH) {
    const batch = pending.slice(start, start + SYNC_BATCH);
    let response: HandoverSyncResponse;
    try {
      response = await send(
        distributionId,
        { handovers: batch.map(toItem) },
        AbortSignal.timeout(SYNC_TIMEOUT_MS),
      );
    } catch (error) {
      // This batch and the later ones were not sent; earlier busy ones are still pending too.
      return { ...stopped(error), recorded, conflicts, pending: pending.length - start + retry };
    }

    // Each handover sent is settled by its own result; one the server did not answer for is sent again.
    const results = new Map(response.results.map((r) => [r.id, r]));
    const done: {
      handover: NonNullable<(typeof response.results)[number]['handover']>;
      sent: { id: string; revision: number };
    }[] = [];
    for (const handover of batch) {
      const sent = { id: handover.id, revision: handover.revision };
      const result = results.get(handover.id);
      if (result?.outcome === 'REFUSED' && result.code !== RETRY_LATER) {
        await markConflict(sent, result.code ?? UNKNOWN_REFUSAL, result.existing);
        conflicts++;
      } else if (result && result.outcome !== 'REFUSED' && result.handover) {
        done.push({ handover: result.handover, sent });
      } else {
        retry++;
      }
    }
    await applySynced(distributionId, ownerUserId, done);
    recorded += done.length;
  }

  return { status: retry > 0 ? 'busy' : 'synced', recorded, conflicts, pending: retry };
}
