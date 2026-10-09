import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { SearchField } from '@/components/app/SearchField';
import { SectionCard } from '@/components/app/SectionCard';
import { StatusBadge } from '@/components/app/StatusBadge';
import { groupHolders, holderName, searchHolders, type CaptureHolder } from './holders';

const ANNOUNCE_DELAY_MS = 500;

interface HolderListProps {
  holders: readonly CaptureHolder[];
  /** Opens the holder's handover panel. */
  onOpen: (holder: CaptureHolder) => void;
}

/**
 * The day's holders grouped by slot and comparsa (spec: Handover screens), searchable by number,
 * name or DNI/NIE, each marked as to deliver, pending, synced or in conflict, with a badge whose text
 * is also part of the row's name. A row opens its handover panel.
 */
export function HolderList({ holders, onOpen }: HolderListProps) {
  const { t } = useTranslation('distribution');
  const [search, setSearch] = useState('');
  const matches = useMemo(() => searchHolders(holders, search), [holders, search]);
  const groups = useMemo(() => groupHolders(matches), [matches]);
  // The number of matches, said once typing pauses rather than on every key (WCAG 4.1.3).
  const [announced, setAnnounced] = useState('');
  const count = search.trim() === '' ? undefined : matches.length;
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setAnnounced(count === undefined ? '' : t('capture.list.results', { count }));
    }, ANNOUNCE_DELAY_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [count, t]);

  return (
    <SectionCard title={t('capture.list.title')} span="full">
      <SearchField label={t('capture.list.search')} value={search} onChange={setSearch} />
      <p role="status" className="sr-only">
        {announced}
      </p>
      {groups.length === 0 && <p className="text-muted-foreground">{t('capture.list.noResults')}</p>}
      {groups.map((group) => {
        const heading = t('capture.list.group', {
          slot: group.slot ?? t('capture.list.noSlot'),
          comparsa: group.comparsaName,
        });
        return (
          <section key={group.key} className="flex flex-col gap-2">
            <h3 className="text-label text-foreground">{heading}</h3>
            {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
            <ul role="list" className="flex flex-col gap-1">
              {group.holders.map((holder) => {
                const { row } = holder;
                return (
                  <li key={row.entryId}>
                    <button
                      type="button"
                      data-entry-id={row.entryId}
                      onClick={() => {
                        onOpen(holder);
                      }}
                      className="flex min-h-11 w-full flex-wrap items-center justify-between gap-x-3 gap-y-1 rounded-md border bg-card px-3 py-2 text-start hover:bg-surface-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    >
                      <span className="flex min-w-0 flex-col">
                        <span className="font-semibold text-foreground">
                          {t('capture.list.holder', { number: row.number, name: holderName(row) })}
                        </span>
                        <span className="text-help text-muted-foreground">
                          {t('capture.list.details', {
                            nationalId: row.nationalId ?? '',
                            kg: row.powderKg,
                            flask: t(`capture.list.flasks.${row.flask}`),
                          })}
                        </span>
                      </span>
                      <StatusBadge kind="handover" value={holder.state} />
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>
        );
      })}
    </SectionCard>
  );
}
