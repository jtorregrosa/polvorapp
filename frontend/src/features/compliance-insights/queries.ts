import type { QueryClient, QueryKey } from '@tanstack/react-query';
import {
  getGetComplianceStatisticsQueryKey,
  getGetComplianceSummaryQueryKey,
} from '@/api/generated/compliance/compliance';

/**
 * How long the warning summary is fresh: the navigation asks for it on every page (design D6), and
 * registry writes invalidate it anyway.
 */
export const SUMMARY_STALE_TIME_MS = 30_000;

/**
 * The query keys of the insights: the summary and the statistics without params, which match every
 * filter combination by prefix (design D6). Orval keys are whole paths, so a shared string prefix
 * would not match them.
 */
export function insightsQueryKeys(): QueryKey[] {
  return [getGetComplianceSummaryQueryKey(), getGetComplianceStatisticsQueryKey()];
}

/** Refetches the insights after a change to the registry, so counts and statistics follow it. */
export async function invalidateInsights(queryClient: QueryClient): Promise<void> {
  await Promise.all(insightsQueryKeys().map((queryKey) => queryClient.invalidateQueries({ queryKey })));
}
