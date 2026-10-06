import { keepPreviousData } from '@tanstack/react-query';
import { useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useListAuditActions, useListAuditEntriesInfinite } from '@/api/generated/audit/audit';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type {
  AuditActionResponse,
  AuditEntryResponse,
  AuditPageResponse,
  ComparsaResponse,
  ListAuditEntriesParams,
  UserResponse,
} from '@/api/generated/model';
import { useListUsers } from '@/api/generated/users/users';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { FilterBar } from '@/components/app/FilterBar';
import { FilterDate } from '@/components/app/FilterDate';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import type { SelectOption } from '@/components/app/SelectInput';
import { isoDaysBefore, todayIso } from '@/lib/dates';
import { useFormatters } from '@/lib/format';
import { withFilter } from '@/lib/search-filters';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { userName } from '@/features/identity-access/user-name';
import { actionLabel, actorLabel, entityTypeLabel, recordHref } from '../audit-labels';
import { AuditEntrySheet } from '../components/AuditEntrySheet';
import { auditProblemMessage } from '../problems';

/** "The last 30 days", today included, when the address names no period (spec: Audit log screens). */
const DEFAULT_DAYS = 30;

const FILTER_KEYS = ['from', 'to', 'actorUserId', 'comparsaId', 'entityType', 'entityId', 'action'] as const;
type FilterKey = (typeof FILTER_KEYS)[number];

/** The filters in the address; the period starts 30 days ago unless it says otherwise. */
function useAuditFilters() {
  const [search, setSearch] = useSearchParams();
  const value = (key: FilterKey): string => search.get(key) ?? '';
  const params: ListAuditEntriesParams = {};
  for (const key of FILTER_KEYS) {
    const current = value(key);
    if (current) params[key] = current;
  }
  // A record filter only means something with its type (the "View history" address has both).
  if (!params.entityType) delete params.entityId;
  if (!search.has('from') && !search.has('to')) {
    params.from = isoDaysBefore(todayIso(), DEFAULT_DAYS - 1);
  }

  const set = (changes: Partial<Record<FilterKey, string>>): void => {
    let next = search;
    for (const [key, changed] of Object.entries(changes)) next = withFilter(next, key, changed);
    // An emptied "From" stays in the address as `from=`: no lower bound, rather than the default again.
    if (changes.from === '') next.set('from', '');
    setSearch(next, { replace: true });
  };

  // The API refuses an inverted period: said at "To" instead, and nothing is asked.
  const inverted = Boolean(params.from && params.to && params.from > params.to);
  return { params, value, set, inverted };
}

/** The options with the current value added when the list does not have it (yet), so the select shows it. */
function withCurrent(options: SelectOption[], value: string, label: string): SelectOption[] {
  return !value || options.some((option) => option.value === value)
    ? options
    : [...options, { value, label }];
}

/** Moves focus to the first filter, when the control that had it goes away (WCAG 2.4.3). */
function focusFilters(): void {
  document.querySelector<HTMLElement>('[data-slot="filter-bar"] :is(input, select)')?.focus();
}

/** Spec audit-privacy "Audit log screens" (UC-25; design D12): who did what and when, for Admins. */
export function AuditLogPage() {
  const { t, i18n } = useTranslation('audit');
  const { t: tIdentity } = useTranslation('identity');
  useDocumentTitle(t('page.title'));
  const format = useFormatters();
  const filters = useAuditFilters();
  const entries = useListAuditEntriesInfinite(filters.params, {
    query: {
      initialPageParam: undefined,
      getNextPageParam: (last) => (last.data as AuditPageResponse).nextCursor ?? undefined,
      enabled: !filters.inverted,
      // A filter change keeps the rows on screen until the new ones arrive.
      placeholderData: keepPreviousData,
    },
  });
  const actions = useListAuditActions();
  const users = useListUsers();
  const comparsas = useListComparsas({ includeInactive: true });
  const list = useRef<HTMLDivElement>(null);

  const rows = useMemo(
    () => (entries.data?.pages ?? []).flatMap((page) => (page.data as AuditPageResponse).items),
    [entries.data],
  );
  const catalogue = useMemo(() => (actions.data?.data ?? []) as AuditActionResponse[], [actions.data]);
  const areas = useMemo(() => [...new Set(catalogue.map((action) => action.entityType))], [catalogue]);

  const columns = useMemo<DataTableColumn<AuditEntryResponse>[]>(
    () => [
      {
        id: 'time',
        header: t('columns.time'),
        cell: (entry) => format.date(new Date(entry.occurredAt), { dateStyle: 'medium', timeStyle: 'short' }),
      },
      { id: 'user', header: t('columns.user'), cell: (entry) => actorLabel(t, entry) },
      {
        id: 'action',
        header: t('columns.action'),
        rowHeader: true,
        cell: (entry) => actionLabel(t, entry.action),
      },
      { id: 'record', header: t('columns.record'), cell: (entry) => <RecordCell entry={entry} /> },
      {
        id: 'comparsa',
        header: t('columns.comparsa'),
        cell: (entry) =>
          entry.comparsaName ?? <span className="text-muted-foreground">{t('record.none')}</span>,
      },
      {
        id: 'details',
        header: t('details.title'),
        hideHeader: true,
        pinned: true,
        align: 'end',
        cell: (entry) => <AuditEntrySheet entry={entry} />,
      },
    ],
    [t, format],
  );

  const collator = useMemo(() => new Intl.Collator(i18n.language), [i18n.language]);
  const byLabel = (x: SelectOption, y: SelectOption) => collator.compare(x.label, y.label);

  /** Appends the next page and moves focus to its first row, so the new entries are where focus is. */
  const showMore = async (): Promise<void> => {
    const before = rows.length;
    const next = await entries.fetchNextPage();
    if (next.isError) return;
    window.requestAnimationFrame(() => {
      const items = list.current?.querySelectorAll('tbody tr, li');
      items?.[before]?.querySelector<HTMLElement>('a, button')?.focus();
    });
  };

  const area = filters.value('entityType');
  const entityId = area ? filters.value('entityId') : '';
  const resultText = entries.isSuccess
    ? t(entries.hasNextPage ? 'page.shownMore' : 'page.shown', { count: rows.length })
    : '';

  return (
    <>
      <PageHeader title={t('page.title')} description={t('page.description')} />
      <FilterBar
        resultText={resultText}
        filters={
          <>
            <FilterDate
              label={t('filters.from')}
              value={filters.params.from ?? ''}
              max={filters.value('to') || todayIso()}
              onChange={(from) => {
                filters.set({ from });
              }}
            />
            <FilterDate
              label={t('filters.to')}
              value={filters.value('to')}
              min={filters.params.from}
              max={todayIso()}
              error={filters.inverted ? t('filters.periodInverted') : undefined}
              onChange={(to) => {
                filters.set({ to });
              }}
            />
            <FilterSelect
              label={t('filters.user')}
              value={filters.value('actorUserId')}
              onChange={(actorUserId) => {
                filters.set({ actorUserId });
              }}
              options={withCurrent(
                [
                  { value: '', label: t('filters.all') },
                  { value: 'none', label: t('filters.noUser') },
                  ...((users.data?.data ?? []) as UserResponse[]).map((user) => ({
                    value: user.id,
                    label: userName(tIdentity, user),
                  })),
                ],
                filters.value('actorUserId'),
                filters.value('actorUserId'),
              )}
            />
            <FilterSelect
              label={t('filters.comparsa')}
              value={filters.value('comparsaId')}
              onChange={(comparsaId) => {
                filters.set({ comparsaId });
              }}
              options={withCurrent(
                [
                  { value: '', label: t('filters.all') },
                  ...((comparsas.data?.data ?? []) as ComparsaResponse[]).map((comparsa) => ({
                    value: comparsa.id,
                    label: comparsa.name,
                  })),
                ],
                filters.value('comparsaId'),
                filters.value('comparsaId'),
              )}
            />
            <FilterSelect
              label={t('filters.area')}
              value={area}
              onChange={(entityType) => {
                const action = catalogue.find((entry) => entry.code === filters.value('action'));
                filters.set({
                  entityType,
                  entityId: '',
                  ...(entityType && action && action.entityType !== entityType ? { action: '' } : {}),
                });
              }}
              options={withCurrent(
                [
                  { value: '', label: t('filters.all') },
                  ...areas.map((type) => ({ value: type, label: entityTypeLabel(t, type) })).sort(byLabel),
                ],
                area,
                entityTypeLabel(t, area),
              )}
            />
            <FilterSelect
              label={t('filters.action')}
              value={filters.value('action')}
              onChange={(action) => {
                filters.set({ action });
              }}
              options={withCurrent(
                [
                  { value: '', label: t('filters.all') },
                  ...catalogue
                    .filter((action) => !area || action.entityType === area)
                    .map((action) => ({ value: action.code, label: actionLabel(t, action.code) }))
                    .sort(byLabel),
                ],
                filters.value('action'),
                actionLabel(t, filters.value('action')),
              )}
            />
          </>
        }
      />
      {(actions.isError || users.isError || comparsas.isError) && (
        <AlertBanner severity="warning">{t('page.filtersFailed')}</AlertBanner>
      )}
      {entityId && (
        <AlertBanner severity="info">
          <span className="flex flex-wrap items-center gap-3">
            {t('filters.record', { type: entityTypeLabel(t, area) })}
            <Button
              variant="secondary"
              size="sm"
              onClick={() => {
                filters.set({ entityId: '' });
                // This banner goes with the filter: focus moves to the filters.
                focusFilters();
              }}
            >
              {t('filters.clearRecord')}
            </Button>
          </span>
        </AlertBanner>
      )}
      {entries.isError && !entries.data ? (
        <AlertBanner severity="error">{auditProblemMessage(t, entries.error)}</AlertBanner>
      ) : (
        <div ref={list} className="grid gap-group">
          <DataTable
            caption={t('page.caption')}
            data={rows}
            columns={columns}
            getRowId={(entry) => entry.id}
            paginated={false}
            emptyText={t('page.empty')}
            isLoading={entries.isPending}
            mobileRow={(entry) => (
              <>
                <span className="font-semibold text-foreground">{actionLabel(t, entry.action)}</span>
                <span className="text-help text-muted-foreground">
                  {format.date(new Date(entry.occurredAt), { dateStyle: 'medium', timeStyle: 'short' })} ·{' '}
                  {actorLabel(t, entry)}
                </span>
                <span className="flex flex-wrap items-center gap-2 text-help">
                  <RecordCell entry={entry} />
                  {entry.comparsaName && <span className="text-muted-foreground">{entry.comparsaName}</span>}
                </span>
                <AuditEntrySheet entry={entry} />
              </>
            )}
          />
          {/* A failed "Show more" keeps what was loaded; the button tries again. */}
          {entries.isFetchNextPageError && (
            <AlertBanner severity="error">{auditProblemMessage(t, entries.error)}</AlertBanner>
          )}
          {entries.hasNextPage && (
            <div className="flex justify-center">
              <Button
                variant="secondary"
                pending={entries.isFetchingNextPage}
                onClick={() => {
                  void showMore();
                }}
              >
                {t('page.showMore')}
              </Button>
            </div>
          )}
        </div>
      )}
    </>
  );
}

/** The entry's record: a link to its page while it exists and has one, its type otherwise. */
function RecordCell({ entry }: { entry: AuditEntryResponse }) {
  const { t } = useTranslation('audit');
  const label = entityTypeLabel(t, entry.entityType);
  const href = recordHref(entry);
  return href ? (
    // Underlined at rest: the same column also holds plain text (WCAG 1.4.1).
    <Link to={href} className="text-foreground underline underline-offset-4 hover:decoration-2">
      {label}
    </Link>
  ) : (
    <span>{label}</span>
  );
}
