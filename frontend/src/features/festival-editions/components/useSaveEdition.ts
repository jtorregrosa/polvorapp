import { useTranslation } from 'react-i18next';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import {
  getGetCurrentEditionQueryKey,
  getGetEditionQueryKey,
  getListEditionsQueryKey,
  useUpdateEdition,
} from '@/api/generated/editions/editions';
import type { EditionResponse, UpdateEditionRequest } from '@/api/generated/model';
import type { EditResult } from '@/components/app/EditSheet';
import { useFormatters } from '@/lib/format';
import { useInvalidate } from '@/lib/use-invalidate';
import { applyFieldErrors, problemCode, problemMessage } from '../problems';

/** Refreshes an edition, the list and the current edition after a change. */
export function useEditionRefresh(id: string): () => Promise<void> {
  return useInvalidate([
    getGetEditionQueryKey(id),
    getListEditionsQueryKey(),
    getGetCurrentEditionQueryKey(),
  ]);
}

/** A translated reason for a failed editions request, with lists in the UI language. */
export function useExplain(): (error: unknown) => string {
  const { t } = useTranslation('editions');
  const { list } = useFormatters();
  return (error) => problemMessage(t, error, list);
}

/** The edit body of `edition` as it stands, for a section to change its part of. */
function bodyOf(edition: EditionResponse): UpdateEditionRequest {
  return {
    festivalStartsOn: edition.festivalStartsOn,
    festivalEndsOn: edition.festivalEndsOn,
    ordersOpenOn: edition.ordersOpenOn,
    ordersCloseOn: edition.ordersCloseOn,
    prices: { ...edition.prices },
    version: edition.version,
  };
}

/**
 * Saves one section of an edition (spec: Detail pages in read mode): the section's values are
 * merged into the edition and sent whole with its version. An outdated version reloads the edition
 * and keeps the panel open with the reason; field errors land on the section's fields.
 */
export function useSaveEdition<TValues extends FieldValues>(edition: EditionResponse) {
  const update = useUpdateEdition();
  const refresh = useEditionRefresh(edition.id);
  const explain = useExplain();

  return async (
    change: Partial<UpdateEditionRequest>,
    fields: readonly Path<TValues>[],
    setError: UseFormSetError<TValues>,
  ): Promise<EditResult> => {
    try {
      await update.mutateAsync({ id: edition.id, data: { ...bodyOf(edition), ...change } });
    } catch (error) {
      if (problemCode(error) === 'editions.modified' || problemCode(error) === 'editions.notFound') {
        await refresh();
        return { status: 'conflict', reason: explain(error) };
      }
      return applyFieldErrors(error, fields, setError)
        ? { status: 'kept' }
        : { status: 'rejected', reason: explain(error) };
    }
    await refresh();
    return { status: 'saved' };
  };
}
