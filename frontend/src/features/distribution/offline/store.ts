import { deleteDB, openDB, type DBSchema, type IDBPDatabase } from 'idb';
import type { CapturePackageResponse, HandoverCollector, HandoverResponse } from '@/api/generated/model';

/**
 * The capture device's store (distribution spec: Data kept on the device; SEC-14;
 * add-offline-distribution-capture D4): the powder day downloaded for offline capture and the
 * handovers captured on the device, in the browser's IndexedDB only — never in the service-worker
 * cache. Everything belongs to the Admin who downloaded or captured it, and every write names that
 * Admin, so one Admin's sync never touches another's data on a shared device.
 */

/** How long a downloaded package is kept (maintainer decision, 2026-10-09). */
export const PACKAGE_LIFETIME_MS = 7 * 24 * 60 * 60 * 1000;

/** How long a handover nobody synced is kept, e.g. another Admin's on a shared device. */
export const QUEUE_LIFETIME_MS = PACKAGE_LIFETIME_MS;

const DATABASE = 'polvorapp-capture';
const VERSION = 1;

/** A downloaded powder day, owned by the Admin who downloaded it. */
export interface StoredPackage {
  distributionId: string;
  ownerUserId: string;
  downloadedAt: string;
  expiresAt: string;
  package: CapturePackageResponse;
}

/** A handover captured on the device: waiting to sync, or refused by the server with its reason. */
export interface CapturedHandover {
  id: string;
  distributionId: string;
  ownerUserId: string;
  holderEntryId: string;
  distributionNumber: number;
  collectedBy: HandoverCollector;
  collectorEntryId: string | null;
  rentalFlaskNumber: string | null;
  traceability1: string | null;
  traceability2: string | null;
  collectedAt: string;
  capturedAt: string;
  /** Grows with every edit: a sync settles only the version it sent (an edit made meanwhile stays). */
  revision: number;
  state: 'pending' | 'conflict';
  code: string | null;
  /** For a conflict with another device: the handover recorded first. */
  existing: HandoverResponse | null;
}

/** What a sync sent of a captured handover: its id and the revision it had then. */
export interface SentRevision {
  id: string;
  revision: number;
}

interface CaptureSchema extends DBSchema {
  packages: { key: string; value: StoredPackage; indexes: { 'by-owner': string } };
  queue: { key: string; value: CapturedHandover; indexes: { 'by-owner': string; 'by-distribution': string } };
}

let database: Promise<IDBPDatabase<CaptureSchema>> | undefined;

function open(): Promise<IDBPDatabase<CaptureSchema>> {
  database ??= openDB<CaptureSchema>(DATABASE, VERSION, {
    upgrade(db) {
      db.createObjectStore('packages', { keyPath: 'distributionId' }).createIndex('by-owner', 'ownerUserId');
      const queue = db.createObjectStore('queue', { keyPath: 'id' });
      queue.createIndex('by-owner', 'ownerUserId');
      queue.createIndex('by-distribution', 'distributionId');
    },
    terminated() {
      // The browser closed the connection (e.g. storage cleared): open it again next time.
      database = undefined;
    },
  }).catch((error: unknown) => {
    // A failed open is retried by the next call instead of failing every call until a reload.
    database = undefined;
    throw error;
  });
  return database;
}

/** Test hook: closes and deletes the database, so each test starts empty. */
export async function resetCaptureDbForTests(): Promise<void> {
  const current = database;
  database = undefined;
  await current?.then(
    (db) => {
      db.close();
    },
    () => undefined,
  );
  await deleteDB(DATABASE);
}

/** Keeps a downloaded package for its Admin, replacing an earlier download of the same day. */
export async function savePackage(
  ownerUserId: string,
  pkg: CapturePackageResponse,
  now: Date,
): Promise<StoredPackage> {
  const stored: StoredPackage = {
    distributionId: pkg.distributionId,
    ownerUserId,
    downloadedAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + PACKAGE_LIFETIME_MS).toISOString(),
    package: pkg,
  };
  await (await open()).put('packages', stored);
  return stored;
}

/** The day's package, when this Admin downloaded it. */
export async function readPackage(
  distributionId: string,
  ownerUserId: string,
): Promise<StoredPackage | undefined> {
  const stored = await (await open()).get('packages', distributionId);
  return stored?.ownerUserId === ownerUserId ? stored : undefined;
}

/**
 * The day's package whoever downloaded it, for the capture screen opened without connectivity, when
 * the session cannot be checked. Another Admin's package never survives their sign-out or someone
 * else's sign-in, so it is the device's current one.
 */
export async function readDevicePackage(distributionId: string): Promise<StoredPackage | undefined> {
  return (await open()).get('packages', distributionId);
}

/** The handovers this Admin captured for the day, pending or in conflict, oldest first. */
export async function listQueue(distributionId: string, ownerUserId: string): Promise<CapturedHandover[]> {
  const all = await (await open()).getAllFromIndex('queue', 'by-distribution', distributionId);
  return all
    .filter((h) => h.ownerUserId === ownerUserId)
    .sort((a, b) => a.capturedAt.localeCompare(b.capturedAt));
}

/**
 * Saves a captured handover, or replaces it when edited before it synced, with a new revision.
 * `replacing` names this Admin's handover it takes the place of, removed in the same transaction:
 * a conflict sent again under a new id never leaves two for one holder.
 */
export async function putCaptured(
  handover: Omit<CapturedHandover, 'revision'> & { revision?: number },
  replacing?: string,
): Promise<void> {
  const db = await open();
  const tx = db.transaction('queue', 'readwrite');
  const current = await tx.store.get(handover.id);
  await tx.store.put({ ...handover, revision: (current?.revision ?? 0) + 1 });
  if (replacing !== undefined && replacing !== handover.id) {
    if ((await tx.store.get(replacing))?.ownerUserId === handover.ownerUserId) {
      await tx.store.delete(replacing);
    }
  }
  await tx.done;
}

/** Removes a captured handover of this Admin: removed by them, or discarded after a conflict. */
export async function removeCaptured(id: string, ownerUserId: string): Promise<void> {
  const db = await open();
  const tx = db.transaction('queue', 'readwrite');
  if ((await tx.store.get(id))?.ownerUserId === ownerUserId) {
    await tx.store.delete(id);
  }
  await tx.done;
}

/**
 * Marks a captured handover as refused by the server, with its reason and the handover recorded
 * first — only when it is still the version sent: an edit made meanwhile is a new attempt.
 */
export async function markConflict(
  sent: SentRevision,
  code: string,
  existing: HandoverResponse | null,
): Promise<void> {
  const db = await open();
  const tx = db.transaction('queue', 'readwrite');
  const handover = await tx.store.get(sent.id);
  if (handover?.revision === sent.revision) {
    await tx.store.put({ ...handover, state: 'conflict', code, existing });
  }
  await tx.done;
}

/**
 * Moves handovers the server recorded into the day's package of this Admin, and out of the queue
 * when they are still the version sent; an edit made meanwhile stays queued and is sent next.
 */
export async function applySynced(
  distributionId: string,
  ownerUserId: string,
  recorded: readonly { handover: HandoverResponse; sent: SentRevision }[],
): Promise<void> {
  const db = await open();
  const tx = db.transaction(['packages', 'queue'], 'readwrite');
  const packages = tx.objectStore('packages');
  const queue = tx.objectStore('queue');
  const stored = await packages.get(distributionId);
  if (stored?.ownerUserId === ownerUserId) {
    const ids = new Set(recorded.map((r) => r.handover.id));
    await packages.put({
      ...stored,
      package: {
        ...stored.package,
        handovers: [
          ...stored.package.handovers.filter((h) => !ids.has(h.id)),
          ...recorded.map((r) => r.handover),
        ],
      },
    });
  }
  for (const { sent } of recorded) {
    const current = await queue.get(sent.id);
    if (current?.ownerUserId === ownerUserId && current.revision === sent.revision) {
      await queue.delete(sent.id);
    }
  }
  await tx.done;
}

/** Drops a handover undone on the server from this Admin's package of the day. */
export async function removeServerHandover(
  distributionId: string,
  ownerUserId: string,
  handoverId: string,
): Promise<void> {
  const db = await open();
  const tx = db.transaction('packages', 'readwrite');
  const stored = await tx.store.get(distributionId);
  if (stored?.ownerUserId === ownerUserId) {
    await tx.store.put({
      ...stored,
      package: { ...stored.package, handovers: stored.package.handovers.filter((h) => h.id !== handoverId) },
    });
  }
  await tx.done;
}

/** How many handovers this Admin has not synced, conflicts included (the sign-out warning). */
export async function unsyncedCount(ownerUserId: string): Promise<number> {
  return (await open()).countFromIndex('queue', 'by-owner', ownerUserId);
}

/** Clears everything of an Admin who signs out on purpose, after the warning. */
export async function clearOwner(ownerUserId: string): Promise<void> {
  const db = await open();
  const tx = db.transaction(['packages', 'queue'], 'readwrite');
  const packages = await tx.objectStore('packages').index('by-owner').getAllKeys(ownerUserId);
  const queue = await tx.objectStore('queue').index('by-owner').getAllKeys(ownerUserId);
  for (const key of packages) await tx.objectStore('packages').delete(key);
  for (const key of queue) await tx.objectStore('queue').delete(key);
  await tx.done;
}

/**
 * Clears an Admin's packages (names and DNI/NIE) when their session ends without the sign-out
 * warning, e.g. "sign out everywhere": their pending handovers, which hold no identity, are kept for
 * them to sync after signing in again.
 */
export async function clearPackages(ownerUserId: string): Promise<void> {
  const db = await open();
  const tx = db.transaction('packages', 'readwrite');
  for (const key of await tx.store.index('by-owner').getAllKeys(ownerUserId)) await tx.store.delete(key);
  await tx.done;
}

/**
 * When someone else signs in: clears the other Admins' packages (names and DNI/NIE), and keeps their
 * pending handovers, which hold no identity, for a week for them to sync.
 */
export async function forgetOtherOwners(currentUserId: string, now: Date): Promise<void> {
  const db = await open();
  const tx = db.transaction(['packages', 'queue'], 'readwrite');
  const packages = await tx.objectStore('packages').getAll();
  const queue = await tx.objectStore('queue').getAll();
  const oldest = now.getTime() - QUEUE_LIFETIME_MS;
  for (const p of packages.filter((p) => p.ownerUserId !== currentUserId)) {
    await tx.objectStore('packages').delete(p.distributionId);
  }
  for (const h of queue.filter((h) => h.ownerUserId !== currentUserId && Date.parse(h.capturedAt) < oldest)) {
    await tx.objectStore('queue').delete(h.id);
  }
  await tx.done;
}

/**
 * On every start: clears packages past their lifetime. The owner's pending handovers stay, without
 * identity, until synced or signed out: downloading the list again shows them (spec: Data kept on
 * the device); another Admin's go a week after capture, when someone else signs in.
 */
export async function purgeExpired(now: Date): Promise<void> {
  const db = await open();
  const tx = db.transaction('packages', 'readwrite');
  for (const p of (await tx.store.getAll()).filter((p) => Date.parse(p.expiresAt) <= now.getTime())) {
    await tx.store.delete(p.distributionId);
  }
  await tx.done;
}

/** Closes the day's capture: clears its package, only once nothing of this Admin is left to sync. */
export async function closeCapture(distributionId: string, ownerUserId: string): Promise<boolean> {
  const db = await open();
  const tx = db.transaction(['packages', 'queue'], 'readwrite');
  const queued = await tx.objectStore('queue').index('by-distribution').getAll(distributionId);
  if (queued.some((h) => h.ownerUserId === ownerUserId)) {
    await tx.done;
    return false;
  }
  if ((await tx.objectStore('packages').get(distributionId))?.ownerUserId === ownerUserId) {
    await tx.objectStore('packages').delete(distributionId);
  }
  await tx.done;
  return true;
}
