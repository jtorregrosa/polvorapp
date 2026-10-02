import { useQueryClient, type QueryKey } from '@tanstack/react-query';
import { useCallback } from 'react';

/**
 * Refreshes what a change affects, e.g. a record and its list: invalidates each key and resolves
 * once the active queries have refetched.
 */
export function useInvalidate(keys: readonly QueryKey[]): () => Promise<void> {
  const queryClient = useQueryClient();
  // Keys are rebuilt on every render; compare their content, not their identity.
  const serialized = JSON.stringify(keys);
  return useCallback(async () => {
    const current = JSON.parse(serialized) as QueryKey[];
    await Promise.all(current.map((queryKey) => queryClient.invalidateQueries({ queryKey })));
  }, [queryClient, serialized]);
}
