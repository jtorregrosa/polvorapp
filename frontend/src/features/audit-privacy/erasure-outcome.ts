import type { TFunction } from 'i18next';
import type { QueryClient } from '@tanstack/react-query';
import type { ErasureResponse } from '@/api/generated/model';

/** The erasure counts the outcome names, in this order; any other count is left out. */
const ERASURE_COUNTS = [
  'arquebusiersDeleted',
  'ownedWeaponsDeleted',
  'photosDeleted',
  'entriesAnonymised',
  'loansAnonymised',
  'pickupProxiesRemoved',
  'usersErased',
  'assignmentsRemoved',
  'notificationPreferencesRemoved',
  'notificationDeliveriesRemoved',
  'auditEntriesRedacted',
] as const;

/** "Data erased. 1 registry record deleted and 2 order entries anonymised." (spec: GDPR request screens). */
export function erasureOutcome(
  t: TFunction<'privacy'>,
  list: (items: readonly string[]) => string,
  result: ErasureResponse,
): string {
  const parts = ERASURE_COUNTS.filter((key) => (result.counts[key] ?? 0) > 0).map((key) =>
    t(`erase.counts.${key}`, { count: result.counts[key] ?? 0 }),
  );
  const done = parts.length > 0 ? `${t('erase.done')} ${list(parts)}.` : t('erase.done');
  return result.filesPending > 0
    ? `${done} ${t('erase.filesPending', { count: result.filesPending })}`
    : done;
}

/**
 * Drops the audit log pages read so far: after an erasure they may still hold the person's names
 * or DNI/NIE in their data, which the audit log now shows redacted.
 */
export function forgetAuditPages(queryClient: QueryClient): void {
  queryClient.removeQueries({ predicate: (query) => query.queryKey.includes('/api/audit-entries') });
}
