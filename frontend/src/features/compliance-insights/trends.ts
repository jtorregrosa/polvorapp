/**
 * Pure helpers of the "Trends" tab (spec: Trends screen): which editions are charted and how the
 * latest one compares with the previous one. The tab turns these into translated sentences.
 */

/** The fields of a trends row these helpers read (the API's `TrendRowResponse`). */
export interface TrendRowLike {
  year: number;
  provisional: boolean;
  active: number;
  reserve: number;
}

/**
 * The editions with entries, oldest first: editions started before any order are not trends, and
 * charting them as zeros would read as a collapse.
 */
export function editionsWithOrders<TRow extends TrendRowLike>(rows: readonly TRow[]): TRow[] {
  return rows.filter((row) => row.active + row.reserve > 0);
}

/** How a figure moved against the previous edition, for the summary sentences. */
export type Change = { kind: 'alone' } | { kind: 'same' } | { kind: 'up' | 'down'; ratio: number };

/**
 * The change of `latest` against `previous` as a share of `previous`. Without a previous figure to
 * divide by (none, or zero) there is no change to state.
 */
export function changeOf(latest: number, previous: number | undefined): Change {
  if (previous === undefined || previous === 0) return { kind: 'alone' };
  if (latest === previous) return { kind: 'same' };
  return { kind: latest > previous ? 'up' : 'down', ratio: Math.abs(latest - previous) / previous };
}

/** A share of `total`, or 0 when there is nothing to share. */
export function shareOf(count: number, total: number): number {
  return total > 0 ? count / total : 0;
}
