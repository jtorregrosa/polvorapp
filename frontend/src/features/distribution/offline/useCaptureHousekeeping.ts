import { useEffect } from 'react';
import { SESSION_ENDED_EVENT } from '@/features/identity-access/session';
import { clearOwner, clearPackages, forgetOtherOwners, purgeExpired, unsyncedCount } from './store';

/**
 * The capture device's housekeeping for the signed-in user (SEC-14): on every start, packages past
 * their week go; when someone signs in, the other Admins' packages go;
 * when a session ends without the sign-out warning, its packages go. A browser without IndexedDB
 * has nothing to clear.
 */
export function useCaptureHousekeeping(userId: string | undefined): void {
  useEffect(() => {
    void purgeExpired(new Date()).catch(() => undefined);
    const ended = (event: Event) => {
      const user = (event as CustomEvent<unknown>).detail;
      if (typeof user === 'string') void clearPackages(user).catch(() => undefined);
    };
    window.addEventListener(SESSION_ENDED_EVENT, ended);
    return () => {
      window.removeEventListener(SESSION_ENDED_EVENT, ended);
    };
  }, []);

  useEffect(() => {
    if (userId) {
      void forgetOtherOwners(userId, new Date()).catch(() => undefined);
    }
  }, [userId]);
}

/**
 * How many handovers the user would lose by signing out now; undefined when the device cannot tell,
 * which the sign-out treats as a reason to warn rather than to go ahead.
 */
export function countUnsynced(userId: string): Promise<number | undefined> {
  // A browser without IndexedDB cannot have stored anything.
  if (typeof indexedDB === 'undefined') return Promise.resolve(0);
  return unsyncedCount(userId).catch(() => undefined);
}

/** Clears the user's capture data after signing out; false when the device could not be cleared. */
export function clearCaptureDevice(userId: string): Promise<boolean> {
  return clearOwner(userId).then(
    () => true,
    () => false,
  );
}
