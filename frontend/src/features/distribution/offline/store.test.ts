import 'fake-indexeddb/auto';
import { afterEach, describe, expect, it } from 'vitest';
import { CAPTURE_PACKAGE, CAPTURED, SERVER_HANDOVER } from '../test-data';
import {
  applySynced,
  clearOwner,
  clearPackages,
  closeCapture,
  forgetOtherOwners,
  listQueue,
  markConflict,
  PACKAGE_LIFETIME_MS,
  purgeExpired,
  putCaptured,
  readDevicePackage,
  readPackage,
  removeCaptured,
  removeServerHandover,
  resetCaptureDbForTests,
  savePackage,
  unsyncedCount,
} from './store';

const ADMIN = '00000000-0000-4000-8000-0000000000a1';
const OTHER = '00000000-0000-4000-8000-0000000000a2';
const DAY = CAPTURE_PACKAGE.distributionId;
const NOW = new Date('2031-04-17T18:00:00Z');
const DAYS = 24 * 60 * 60 * 1000;

const recorded = (id: string, revision = 1) => ({
  handover: { ...SERVER_HANDOVER, id },
  sent: { id, revision },
});

afterEach(async () => {
  await resetCaptureDbForTests();
});

describe('capture store (distribution spec: Data kept on the device, SEC-14)', () => {
  it('keeps a package for its Admin, with when it was downloaded and when it will be cleared', async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, NOW);

    const stored = await readPackage(DAY, ADMIN);

    expect(stored?.package.rows).toHaveLength(CAPTURE_PACKAGE.rows.length);
    expect(stored?.downloadedAt).toBe(NOW.toISOString());
    expect(stored?.expiresAt).toBe(new Date(NOW.getTime() + PACKAGE_LIFETIME_MS).toISOString());
    expect(await readPackage(DAY, OTHER)).toBeUndefined();
  });

  it('queues captured handovers per day and Admin, and edits or removes one not yet synced', async () => {
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN });
    await putCaptured({ ...CAPTURED, id: 'other-admin', ownerUserId: OTHER });

    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, rentalFlaskNumber: 'P-200' });
    expect(await listQueue(DAY, ADMIN)).toEqual([
      { ...CAPTURED, ownerUserId: ADMIN, rentalFlaskNumber: 'P-200', revision: 2 },
    ]);
    expect(await unsyncedCount(ADMIN)).toBe(1);

    await removeCaptured(CAPTURED.id, OTHER);
    expect(await listQueue(DAY, ADMIN)).toHaveLength(1);
    await removeCaptured(CAPTURED.id, ADMIN);
    expect(await listQueue(DAY, ADMIN)).toEqual([]);
    expect(await listQueue(DAY, OTHER)).toHaveLength(1);
  });

  it('moves synced handovers into the package and keeps refused ones as conflicts', async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, NOW);
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN });
    await putCaptured({ ...CAPTURED, id: 'refused', ownerUserId: ADMIN, holderEntryId: 'holder-2' });

    await applySynced(DAY, ADMIN, [recorded(CAPTURED.id)]);
    await markConflict({ id: 'refused', revision: 1 }, 'distribution.flaskNumberTaken', null);

    expect(await listQueue(DAY, ADMIN)).toEqual([
      expect.objectContaining({ id: 'refused', state: 'conflict', code: 'distribution.flaskNumberTaken' }),
    ]);
    expect((await readPackage(DAY, ADMIN))?.package.handovers.map((h) => h.id)).toContain(CAPTURED.id);
    // The conflict is not synced either: signing out would lose it.
    expect(await unsyncedCount(ADMIN)).toBe(1);
  });

  it('keeps an edit made while its earlier version was syncing', async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, NOW);
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN });
    await putCaptured({ ...CAPTURED, id: 'refused', ownerUserId: ADMIN });
    // Both edited after revision 1 was sent.
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, rentalFlaskNumber: 'P-200' });
    await putCaptured({ ...CAPTURED, id: 'refused', ownerUserId: ADMIN, rentalFlaskNumber: 'P-300' });

    await applySynced(DAY, ADMIN, [recorded(CAPTURED.id)]);
    await markConflict({ id: 'refused', revision: 1 }, 'distribution.flaskNumberTaken', null);

    expect((await listQueue(DAY, ADMIN)).map((h) => [h.id, h.state, h.rentalFlaskNumber])).toEqual([
      [CAPTURED.id, 'pending', 'P-200'],
      ['refused', 'pending', 'P-300'],
    ]);
  });

  it('sends a conflict again under a new id, in place of the old one', async () => {
    await putCaptured({
      ...CAPTURED,
      ownerUserId: ADMIN,
      state: 'conflict',
      code: 'distribution.flaskNumberTaken',
    });
    await putCaptured({ ...CAPTURED, id: 'other-admins', ownerUserId: OTHER, state: 'conflict' });

    await putCaptured(
      { ...CAPTURED, id: 'again', ownerUserId: ADMIN, state: 'pending', code: null },
      CAPTURED.id,
    );
    await putCaptured({ ...CAPTURED, id: 'not-mine', ownerUserId: ADMIN }, 'other-admins');

    expect((await listQueue(DAY, ADMIN)).map((h) => h.id).sort()).toEqual(['again', 'not-mine']);
    expect((await listQueue(DAY, OTHER)).map((h) => h.id)).toEqual(['other-admins']);
  });

  it("never writes into another Admin's package", async () => {
    await savePackage(OTHER, { ...CAPTURE_PACKAGE, handovers: [SERVER_HANDOVER] }, NOW);

    await applySynced(DAY, ADMIN, [recorded('new')]);
    await removeServerHandover(DAY, ADMIN, SERVER_HANDOVER.id);

    expect((await readPackage(DAY, OTHER))?.package.handovers).toEqual([SERVER_HANDOVER]);
  });

  it('drops an undone handover from the package', async () => {
    await savePackage(ADMIN, { ...CAPTURE_PACKAGE, handovers: [SERVER_HANDOVER] }, NOW);

    await removeServerHandover(DAY, ADMIN, SERVER_HANDOVER.id);

    expect((await readPackage(DAY, ADMIN))?.package.handovers).toEqual([]);
  });

  it('clears everything of an Admin who signs out, and nothing of anyone else', async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, NOW);
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN });
    await putCaptured({ ...CAPTURED, id: 'kept', ownerUserId: OTHER });

    await clearOwner(ADMIN);

    expect(await readPackage(DAY, ADMIN)).toBeUndefined();
    expect(await listQueue(DAY, ADMIN)).toEqual([]);
    expect(await listQueue(DAY, OTHER)).toHaveLength(1);
  });

  it("clears a user's packages when their session ends elsewhere, keeping their queue", async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, NOW);
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN });

    await clearPackages(ADMIN);

    expect(await readPackage(DAY, ADMIN)).toBeUndefined();
    expect(await listQueue(DAY, ADMIN)).toHaveLength(1);
  });

  it("clears another Admin's package when someone else signs in, keeping their pending handovers a week", async () => {
    await savePackage(OTHER, CAPTURE_PACKAGE, NOW);
    await putCaptured({ ...CAPTURED, ownerUserId: OTHER, capturedAt: NOW.toISOString() });
    await putCaptured({
      ...CAPTURED,
      id: 'old',
      ownerUserId: OTHER,
      capturedAt: new Date(NOW.getTime() - 8 * DAYS).toISOString(),
    });

    await forgetOtherOwners(ADMIN, NOW);

    expect(await readPackage(DAY, OTHER)).toBeUndefined();
    expect((await listQueue(DAY, OTHER)).map((h) => h.id)).toEqual([CAPTURED.id]);
    expect(await unsyncedCount(ADMIN)).toBe(0);
  });

  it('shows the device package to a capture screen that cannot check the session', async () => {
    await savePackage(OTHER, CAPTURE_PACKAGE, NOW);

    expect((await readDevicePackage(DAY))?.ownerUserId).toBe(OTHER);
  });

  it("clears a package 7 days after its download, keeping its owner's pending handovers", async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, NOW);
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, capturedAt: NOW.toISOString() });

    await purgeExpired(new Date(NOW.getTime() + 6 * DAYS));
    expect(await readPackage(DAY, ADMIN)).toBeDefined();

    await purgeExpired(new Date(NOW.getTime() + 8 * DAYS));
    expect(await readPackage(DAY, ADMIN)).toBeUndefined();
    expect(await listQueue(DAY, ADMIN)).toHaveLength(1);
  });

  it('closes the capture only when nothing is left to sync', async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, NOW);
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN });

    expect(await closeCapture(DAY, ADMIN)).toBe(false);
    expect(await readPackage(DAY, ADMIN)).toBeDefined();

    await removeCaptured(CAPTURED.id, ADMIN);
    expect(await closeCapture(DAY, ADMIN)).toBe(true);
    expect(await readPackage(DAY, ADMIN)).toBeUndefined();
  });
});
