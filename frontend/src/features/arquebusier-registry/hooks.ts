import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getGetArquebusierQueryKey,
  getListArquebusiersQueryKey,
} from '@/api/generated/arquebusiers/arquebusiers';
import type { WeaponModelSummary } from '@/api/generated/model';
import { invalidateInsights } from '@/features/compliance-insights/queries';

/**
 * Refreshes what a change to the arquebusier affects: its page and the list, awaited, and the
 * insights (the warning count, the dashboard and the statistics) in the background, so a save
 * never waits for them.
 */
export function useRefreshArquebusier(id: string): () => Promise<void> {
  const queryClient = useQueryClient();
  return useCallback(async () => {
    void invalidateInsights(queryClient);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetArquebusierQueryKey(id) }),
      queryClient.invalidateQueries({ queryKey: getListArquebusiersQueryKey() }),
    ]);
  }, [id, queryClient]);
}

/**
 * After a change someone else made (e.g. a version conflict): the list and the insights may be
 * outdated too, so they refetch in the background while the page shows the current record.
 */
export function useRefreshRegistryViews(): () => void {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: getListArquebusiersQueryKey() });
    void invalidateInsights(queryClient);
  }, [queryClient]);
}

/** The model's Federation label, marked when it has been retired from the catalogue. */
export function useModelLabel(): (model: WeaponModelSummary) => string {
  const { t } = useTranslation('registry');
  return useCallback(
    (model) => (model.active ? model.label : t('ownedWeapons.form.inactiveModel', { label: model.label })),
    [t],
  );
}
