import { useGetComplianceSummary } from '@/api/generated/compliance/compliance';
import type { ComplianceSummaryResponse } from '@/api/generated/model';
import { useSession } from '@/features/identity-access/session';
import { SUMMARY_STALE_TIME_MS } from '../queries';

/**
 * How many arquebusiers in the user's scope have at least one warning, for the navigation (spec:
 * Warning count in the navigation); undefined while loading or when the summary fails, so the
 * shell then shows no count. Registry writes invalidate it (`invalidateInsights`).
 */
export function useWarningCount(): number | undefined {
  const signedIn = useSession().status === 'signedIn';
  const summary = useGetComplianceSummary({ query: { enabled: signedIn, staleTime: SUMMARY_STALE_TIME_MS } });
  return summary.isSuccess ? (summary.data.data as ComplianceSummaryResponse).withWarnings : undefined;
}
