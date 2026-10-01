import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getGetArquebusierQueryKey,
  getListArquebusiersQueryKey,
} from '@/api/generated/arquebusiers/arquebusiers';
import type { WeaponModelSummary } from '@/api/generated/model';

/** Refreshes what a change to the arquebusier affects: its page and the list. */
export function useRefreshArquebusier(id: string): () => Promise<void> {
  const queryClient = useQueryClient();
  return useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetArquebusierQueryKey(id) }),
      queryClient.invalidateQueries({ queryKey: getListArquebusiersQueryKey() }),
    ]);
  }, [id, queryClient]);
}

/** The model's Federation label, marked when it has been retired from the catalogue. */
export function useModelLabel(): (model: WeaponModelSummary) => string {
  const { t } = useTranslation('registry');
  return useCallback(
    (model) => (model.active ? model.label : t('ownedWeapons.form.inactiveModel', { label: model.label })),
    [t],
  );
}
