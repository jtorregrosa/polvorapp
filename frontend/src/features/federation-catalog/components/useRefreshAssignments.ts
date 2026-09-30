import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

const ASSIGNMENT_LISTS = /^\/api\/(comparsas\/[^/]+\/firing-chiefs|firing-chiefs\/[^/]+\/comparsas)$/;

/**
 * Refreshes every list of assignments, from both sides: a change made on a comparsa's page also
 * shows on the FiringChief's user page, and the other way round. A rejected change refreshes too,
 * so the lists reflect what the server holds (spec: "Rejected assignment shows its reason").
 */
export function useRefreshAssignments(): () => Promise<void> {
  const queryClient = useQueryClient();
  return useCallback(
    () =>
      queryClient.invalidateQueries({
        predicate: (query) => ASSIGNMENT_LISTS.test(String(query.queryKey[0])),
      }),
    [queryClient],
  );
}
