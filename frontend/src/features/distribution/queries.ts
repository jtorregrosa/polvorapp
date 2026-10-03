import { useQueryClient, type Query } from '@tanstack/react-query';
import { useCallback } from 'react';
import {
  getGetDistributionPlanQueryKey,
  getListPickupProxiesQueryKey,
} from '@/api/generated/distribution/distribution';
import type { DistributionPlanResponse } from '@/api/generated/model';

/**
 * Refreshes an edition's distribution after a change or a refusal: its plan, its proxies and the
 * proxy candidates of every comparsa (a day's date changes whose license holds). Resolves with the
 * plan as it is now, for a panel to show the current values; undefined when it could not be read.
 */
export function useDistributionRefresh(
  editionId: string,
): () => Promise<DistributionPlanResponse | undefined> {
  const queryClient = useQueryClient();
  return useCallback(async () => {
    const planKey = getGetDistributionPlanQueryKey(editionId);
    const [proxies] = getListPickupProxiesQueryKey(editionId);
    const candidates = `/api/distribution/editions/${editionId}/comparsas/`;
    const isCandidates = (query: Query) =>
      typeof query.queryKey[0] === 'string' && query.queryKey[0].startsWith(candidates);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: planKey }),
      queryClient.invalidateQueries({ queryKey: [proxies] }),
      queryClient.invalidateQueries({ predicate: isCandidates }),
    ]);
    const state = queryClient.getQueryState(planKey);
    const plan = queryClient.getQueryData<{ data: unknown }>(planKey)?.data as
      DistributionPlanResponse | undefined;
    return state?.status === 'error' ? undefined : plan;
  }, [queryClient, editionId]);
}
