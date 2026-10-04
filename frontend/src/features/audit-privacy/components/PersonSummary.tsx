import { useMemo, type ReactNode, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import type { PersonEntryResponse, PersonLookupResponse } from '@/api/generated/model';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { DescriptionList } from '@/components/app/DescriptionList';
import { SectionCard } from '@/components/app/SectionCard';
import { StatusBadge } from '@/components/app/StatusBadge';

/** Spec "Looking up a person (UC-26)": what PolvorApp holds about the person, by area. */
export function PersonSummary({
  person,
  actions,
  ref,
}: {
  person: PersonLookupResponse;
  actions: ReactNode;
  /** The section, focusable, to move focus to when the result arrives. */
  ref?: Ref<HTMLElement>;
}) {
  const { t } = useTranslation('privacy');
  const unknownComparsa = t('summary.registry.unknownComparsa');

  const columns = useMemo<DataTableColumn<PersonEntryResponse & { rowId: string }>[]>(
    () => [
      {
        id: 'edition',
        header: t('summary.entries.edition'),
        rowHeader: true,
        cell: (entry) => entry.editionYear,
      },
      {
        id: 'comparsa',
        header: t('summary.entries.comparsa'),
        cell: (entry) => entry.comparsaName ?? unknownComparsa,
      },
      {
        id: 'orderStatus',
        header: t('summary.entries.orderStatus'),
        cell: (entry) =>
          entry.erased ? t('summary.entries.erased') : <StatusBadge kind="order" value={entry.orderStatus} />,
      },
    ],
    [t, unknownComparsa],
  );

  // Entries have no id in the lookup: their position in the API's answer identifies them.
  const entries = useMemo(
    () => person.entries.map((entry, index) => ({ ...entry, rowId: `${entry.editionYear}-${index}` })),
    [person.entries],
  );
  const registry = person.registry;
  return (
    <SectionCard ref={ref} anchorId="privacy-summary" title={t('summary.title')} action={actions} span="full">
      <div className="grid gap-section">
        <h3 className="text-label text-foreground">{t('summary.registry.title')}</h3>
        {registry ? (
          <DescriptionList
            items={[
              { term: t('summary.registry.name'), value: `${registry.firstName} ${registry.lastName}` },
              { term: t('summary.registry.comparsa'), value: registry.comparsaName ?? unknownComparsa },
              {
                term: t('summary.registry.status'),
                value: <StatusBadge kind="arquebusier" value={registry.status} />,
              },
              { term: t('summary.registry.ownedWeapons'), value: registry.ownedWeapons },
              { term: t('summary.registry.photos'), value: registry.photos },
            ]}
          />
        ) : (
          <p className="text-body text-muted-foreground">{t('summary.registry.none')}</p>
        )}
        <h3 className="text-label text-foreground">{t('summary.entries.title')}</h3>
        <DataTable
          caption={t('summary.entries.caption')}
          data={entries}
          columns={columns}
          getRowId={(entry) => entry.rowId}
          paginated={false}
          emptyText={t('summary.entries.none')}
        />
        <DescriptionList
          items={[
            {
              term: t('summary.lenderLoans.title'),
              value:
                person.lenderLoans.length > 0 ? (
                  <ul className="grid gap-1">
                    {person.lenderLoans.map((loans) => (
                      <li key={loans.editionYear}>
                        {t('summary.lenderLoans.item', { year: loans.editionYear, count: loans.loans })}
                      </li>
                    ))}
                  </ul>
                ) : (
                  t('summary.lenderLoans.none')
                ),
            },
            {
              term: t('summary.pickupProxies.label'),
              value: t('summary.pickupProxies.count', { count: person.pickupProxies }),
            },
          ]}
        />
      </div>
    </SectionCard>
  );
}
