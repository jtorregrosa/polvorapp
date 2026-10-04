import { useTranslation } from 'react-i18next';
import type { AuditEntryResponse } from '@/api/generated/model';
import { DescriptionList, type DescriptionItem } from '@/components/app/DescriptionList';
import { DetailSheet } from '@/components/app/DetailSheet';
import { useFormatters } from '@/lib/format';
import { actionLabel, actorLabel, entityTypeLabel } from '../audit-labels';

type Plain = string | number | boolean | null;

const isPlain = (value: unknown): value is Plain =>
  value === null || ['string', 'number', 'boolean'].includes(typeof value);

/** A recorded value as text: lists of plain values joined, anything else as JSON. */
function shown(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (isPlain(value)) return String(value);
  if (Array.isArray(value) && value.every(isPlain)) return value.map(String).join(', ');
  return JSON.stringify(value);
}

/** The recorded data fields, each with its value; none when the entry recorded no object. */
function dataItems(data: unknown): DescriptionItem[] {
  if (data === null || typeof data !== 'object' || Array.isArray(data)) return [];
  return Object.entries(data).map(([field, value]) => ({ term: field, value: shown(value), mono: true }));
}

/** An audit entry's details (spec: Audit log screens): who, what, which record, and what was recorded. */
export function AuditEntrySheet({ entry }: { entry: AuditEntryResponse }) {
  const { t } = useTranslation('audit');
  const format = useFormatters();
  const time = format.date(new Date(entry.occurredAt), { dateStyle: 'medium', timeStyle: 'short' });
  return (
    <DetailSheet
      title={t('details.title')}
      description={t('details.description')}
      trigger={{ context: `${actionLabel(t, entry.action)}, ${time}` }}
    >
      <AuditEntryDetails entry={entry} />
    </DetailSheet>
  );
}

/** The panel's content, rendered only while it is open. */
function AuditEntryDetails({ entry }: { entry: AuditEntryResponse }) {
  const { t } = useTranslation('audit');
  const format = useFormatters();
  const data = dataItems(entry.data);
  return (
    <div className="grid gap-section">
      <DescriptionList
        items={[
          {
            term: t('details.time'),
            value: format.date(new Date(entry.occurredAt), { dateStyle: 'long', timeStyle: 'medium' }),
          },
          { term: t('details.user'), value: actorLabel(t, entry) },
          { term: t('details.action'), value: actionLabel(t, entry.action) },
          { term: t('details.record'), value: entityTypeLabel(t, entry.entityType) },
          { term: t('details.recordId'), value: entry.entityId, mono: true },
          { term: t('details.comparsa'), value: entry.comparsaName },
          { term: t('details.traceId'), value: entry.traceId, mono: true },
        ]}
      />
      <section className="grid gap-3" aria-labelledby={`audit-data-${entry.id}`}>
        <h3 id={`audit-data-${entry.id}`} className="text-label text-foreground">
          {t('details.data')}
        </h3>
        {data.length > 0 ? (
          <DescriptionList items={data} />
        ) : (
          <p className="text-help text-muted-foreground">{t('details.noData')}</p>
        )}
      </section>
    </div>
  );
}
