import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';

/**
 * New recovery codes travel from the page that created them to the page that shows them in memory
 * only (the query cache), never in the browser history: a reload or the back button does not show
 * them again (spec: Mandatory two-factor enrolment — codes shown once).
 */
const RECOVERY_CODES_KEY = ['identity-access', 'new-recovery-codes'] as const;

export function handOverRecoveryCodes(queryClient: QueryClient, codes: readonly string[]): void {
  queryClient.setQueryData(RECOVERY_CODES_KEY, codes);
}

/** The handed-over codes (read once per mount) and a function that forgets them. */
export function useHandedOverRecoveryCodes(): { codes: readonly string[]; forget: () => void } {
  const queryClient = useQueryClient();
  const [codes] = useState(() => queryClient.getQueryData<readonly string[]>(RECOVERY_CODES_KEY) ?? []);
  const forget = useCallback(() => {
    queryClient.removeQueries({ queryKey: RECOVERY_CODES_KEY });
  }, [queryClient]);
  return { codes, forget };
}
