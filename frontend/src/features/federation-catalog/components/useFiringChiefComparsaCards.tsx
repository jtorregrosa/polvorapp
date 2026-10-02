import { useMemo } from 'react';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type { ComparsaResponse } from '@/api/generated/model';
import type { SidebarCard } from '@/components/app/AppLayout';
import { ComparsaLogo } from '@/components/app/ComparsaLogo';
import { useSession } from '@/features/identity-access/session';
import { logoUrl } from '../logos';

const NO_CARDS: readonly SidebarCard[] = [];

/**
 * The sidebar cards of a FiringChief (platform spec: Application shell; add-comparsa-logos D8): one
 * per active comparsa in their scope, in the server's order (by name), with the logo or the
 * placeholder. Admins get none and ask for nothing. While loading, or when the list fails, there
 * are no cards: the comparsas page reports its own failure.
 */
export function useFiringChiefComparsaCards(): readonly SidebarCard[] {
  const session = useSession();
  const isFiringChief = session.status === 'signedIn' && session.account.role === 'FIRING_CHIEF';
  // Active only (the API's default) and scoped by the server (BR-12). The shell stays mounted for
  // the whole session, so the cards refresh when the FiringChief comes back to the window: an Admin
  // may have changed a logo, a name or an assignment meanwhile.
  const comparsas = useListComparsas(undefined, {
    query: { enabled: isFiringChief, refetchOnWindowFocus: true },
  });
  const rows = comparsas.data?.data as ComparsaResponse[] | undefined;

  return useMemo(() => {
    if (!isFiringChief || !rows) return NO_CARDS;
    return rows.map((comparsa) => ({
      to: `/comparsas/${comparsa.id}`,
      label: comparsa.name,
      media: <ComparsaLogo src={logoUrl(comparsa.id, comparsa.logo)} size="md" />,
    }));
  }, [isFiringChief, rows]);
}
