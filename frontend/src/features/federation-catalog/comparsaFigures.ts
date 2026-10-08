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

/** Every comparsa's figures in the user's scope, and the year of the current edition. */
export interface AllComparsaFigures {
  byComparsa: ReadonlyMap<string, ComparsaFigures>;
  year?: number;
}

/** A request behind the figures that failed: what is shown is incomplete. */
export interface FiguresFailure {
  error: unknown;
  retry: () => void;
}

/**
 * The figures of every comparsa in the user's scope (UI audit, item 17), from the arquebusier list
 * and the current edition's orders, both already cached by their own pages. `figures` is undefined
 * while either loads or when the arquebusiers failed; a failed request is reported in `failure`, so
 * missing orders never look like "no edition in progress".
 */
export function useComparsaFigures(): { figures?: AllComparsaFigures; failure?: FiguresFailure } {
  const enabled = useSession().status === 'signedIn';
  const arquebusiers = useListArquebusiers({}, { query: { enabled } });
  const overview = useGetComparsaOrdersOverview(undefined, { query: { enabled } });
  // Answered once, even if failed: a retry keeps what is shown instead of going back to loading.
  const overviewSettled = overview.isFetched;
  const figures = useMemo(() => {
    if (!arquebusiers.isSuccess || !overviewSettled) return undefined;
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
      const counted = figuresOf(row.comparsaId);
      if (row.status === 'ACTIVE') {
        counted.active += 1;
        if (row.warnings.length > 0) counted.activeWithWarnings += 1;
      } else {
        counted.reserve += 1;
      }
    }
    for (const row of orders?.rows ?? []) figuresOf(row.comparsa.id).order = row;
    return { byComparsa, year: orders?.edition?.year };
  }, [arquebusiers.isSuccess, arquebusiers.data, overviewSettled, overview.data]);

  // The last answer was an error: still true while it is retried, so the notice and its button stay.
  const failed = [arquebusiers, overview].filter((query) => query.errorUpdatedAt > query.dataUpdatedAt);
  const failure: FiguresFailure | undefined =
    failed.length > 0
      ? {
          error: failed[0]?.error ?? undefined,
          retry: () => {
            for (const query of failed) void query.refetch();
          },
        }
      : undefined;
  return { figures, failure };
}
