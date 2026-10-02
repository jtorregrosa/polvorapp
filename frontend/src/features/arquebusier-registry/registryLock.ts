import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import type { RegistryLockResponse } from '@/api/generated/model';
import { getGetRegistryLockQueryKey, useGetRegistryLock } from '@/api/generated/registry/registry';
import { useSession } from '@/features/identity-access/session';
import { problemCode } from './problems';

export interface RegistryLockState {
  /** Whether the Federation has locked the registry (BR-10). */
  locked: boolean;
  isAdmin: boolean;
  /** Whether the caller may change the registry: Admins always, FiringChiefs while it is unlocked. */
  canWrite: boolean;
  /** The lock arrived as a refused change on this page: the notice is announced, not just shown. */
  lockedMeanwhile: boolean;
  /** Whether the lock is known yet; until then nothing is offered that depends on it. */
  known: boolean;
}

/**
 * The registry lock as the caller sees it (spec: Registry lock screens). The server decides every
 * write; the UI only hides what a FiringChief could not do. A write refused because the registry
 * was locked meanwhile (`registry.locked`) reloads the lock, so the page switches to the locked state.
 */
export function useRegistryLock(): RegistryLockState {
  const session = useSession();
  const queryClient = useQueryClient();
  const isAdmin = session.account?.role === 'ADMIN';
  const lock = useGetRegistryLock({ query: { enabled: session.status === 'signedIn' } });
  const [lockedMeanwhile, setLockedMeanwhile] = useState(false);

  useEffect(
    () =>
      queryClient.getMutationCache().subscribe((event) => {
        if (event.type === 'updated' && problemCode(event.mutation.state.error) === 'registry.locked') {
          setLockedMeanwhile(true);
          void queryClient.invalidateQueries({ queryKey: getGetRegistryLockQueryKey() });
        }
      }),
    [queryClient],
  );

  const locked = (lock.data?.data as RegistryLockResponse | undefined)?.locked ?? false;
  // While loading, a FiringChief's write actions wait, so they never flash and vanish. If the lock
  // cannot be read they are offered: the server refuses them anyway while it is locked.
  const known = lock.isSuccess;
  const canWrite = isAdmin || (known ? !locked : lock.isError);
  return { locked, isAdmin, canWrite, lockedMeanwhile: locked && lockedMeanwhile, known };
}
