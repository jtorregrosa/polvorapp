import { useMemo } from 'react';
import { useListArquebusiers } from '@/api/generated/arquebusiers/arquebusiers';
import { useGetComparsaOrdersOverview } from '@/api/generated/comparsa-orders/comparsa-orders';
import type { ArquebusierRowResponse, OverviewResponse, OverviewRowResponse } from '@/api/generated/model';
import { useSession } from '@/features/identity-access/session';

/** A comparsa's figures in the user's scope: its arquebusiers and its order of the current edition. */
export interface ComparsaFigures {
  active: number;
  reserve: number;
  /** Active arquebusiers with at least one compliance warning, as the server derives them. */
  activeWithWarnings: number;
  /** The comparsa's row in the current edition's orders; undefined without an edition in progress. */
  order?: OverviewRowResponse;
}

/**
 * The figures of every comparsa in the user's scope (UI audit, item 17), from the arquebusier list
 * and the current edition's orders, both already cached by their own pages; undefined while either
 * loads. The year of the edition comes with it.
 */
export function useComparsaFigures():
  { byComparsa: ReadonlyMap<string, ComparsaFigures>; year?: number } | undefined {
  const enabled = useSession().status === 'signedIn';
  const arquebusiers = useListArquebusiers({}, { query: { enabled } });
  const overview = useGetComparsaOrdersOverview(undefined, { query: { enabled } });
  return useMemo(() => {
    if (!arquebusiers.isSuccess) return undefined;
    const rows = arquebusiers.data.data as ArquebusierRowResponse[];
    const orders = overview.data?.data as OverviewResponse | undefined;
    const byComparsa = new Map<string, ComparsaFigures>();
    const figuresOf = (id: string): ComparsaFigures => {
      const known = byComparsa.get(id);
      if (known) return known;
      const created: ComparsaFigures = { active: 0, reserve: 0, activeWithWarnings: 0 };
      byComparsa.set(id, created);
      return created;
    };
    for (const row of rows) {
      const figures = figuresOf(row.comparsaId);
      if (row.status === 'ACTIVE') {
        figures.active += 1;
        if (row.warnings.length > 0) figures.activeWithWarnings += 1;
      } else {
        figures.reserve += 1;
      }
    }
    for (const row of orders?.rows ?? []) figuresOf(row.comparsa.id).order = row;
    return { byComparsa, year: orders?.edition?.year };
  }, [arquebusiers.isSuccess, arquebusiers.data, overview.data]);
}
