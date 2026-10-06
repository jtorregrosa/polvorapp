import { FileUp, IdCard, Plus } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useListArquebusiers } from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type { ArquebusierRowResponse, ComparsaResponse, ListArquebusiersParams } from '@/api/generated/model';
import { NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DataTable, type RowSelection } from '@/components/app/DataTable';
import { EmptyState } from '@/components/app/EmptyState';
import { FilterBar, NoMatches } from '@/components/app/FilterBar';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { SearchField } from '@/components/app/SearchField';
import { StatFilter } from '@/components/app/StatFilter';
import { StatusBadge } from '@/components/app/StatusBadge';
import { BadgeSheet } from '@/features/badges/components/BadgeSheet';
import { MAX_BADGES } from '@/features/badges/batch';
import { useSession } from '@/features/identity-access/session';
import { useNotice } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { LoadFailure } from '../components/LoadFailure';
import { RegistryLockNotice } from '../components/RegistryLock';
import { useRegistryLockAction } from '../components/useRegistryLockAction';
import { useRegistryLock } from '../registryLock';
import { useArquebusierColumns } from '../components/useArquebusierColumns';
import { filterRows, useArquebusierFilters, WARNING_FILTERS } from '../components/useArquebusierFilters';

/** Pause after the last keystroke before the count is shown and announced, so it is not read per letter. */
const ANNOUNCE_DELAY_MS = 400;

/** `text` once it has settled for a moment, so typing a search does not announce every letter. */
function useSettled(text: string): string {
  const [settled, setSettled] = useState(text);
  useEffect(() => {
    const timer = setTimeout(() => {
      setSettled(text);
    }, ANNOUNCE_DELAY_MS);
    return () => {
      clearTimeout(timer);
    };
  }, [text]);
  return settled;
}

/** The Admin's badge selection (spec: Badge screens): the selected rows, kept across filters and pages. */
interface BadgeSelection {
  rows: readonly ArquebusierRowResponse[];
  change: (selection: RowSelection) => void;
  remove: (ids: readonly string[]) => void;
  clear: () => void;
}

interface ListProps {
  rows: readonly ArquebusierRowResponse[];
  loaded: boolean;
  loading: boolean;
  comparsas: readonly ComparsaResponse[];
  filters: ReturnType<typeof useArquebusierFilters>;
  /** Admins only. */
  selection?: BadgeSelection;
}

const rowLabel = (row: ArquebusierRowResponse) => `${row.lastName}, ${row.firstName}`;

/** The table's selection props for Admins; none for FiringChiefs. */
function selectionProps(selection: BadgeSelection | undefined):
  | {
      rowSelection: RowSelection;
      onRowSelectionChange: (next: RowSelection) => void;
      getRowLabel: typeof rowLabel;
    }
  | Record<string, never> {
  if (!selection) return {};
  return {
    rowSelection: Object.fromEntries(selection.rows.map((row) => [row.id, true])),
    onRowSelectionChange: selection.change,
    getRowLabel: rowLabel,
  };
}

/** How many are selected, "Clear", and "Print badges" for them, refused beyond {@link MAX_BADGES}. */
function SelectionBar({ selection }: { selection: BadgeSelection }) {
  const { t } = useTranslation('badges');
  const count = selection.rows.length;
  const tooMany = count > MAX_BADGES;
  const reasonId = useId();
  return (
    <section
      aria-label={t('selection.label')}
      className="flex flex-col gap-2 rounded-lg border bg-surface-2 px-4 py-3 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between"
    >
      <div className="flex flex-col gap-1">
        <p className="font-medium">{t('selection.count', { count })}</p>
        {/* Always rendered, so the limit is announced the moment it is passed. */}
        <p id={reasonId} aria-live="polite" className="text-help text-muted-foreground empty:hidden">
          {tooMany ? t('selection.limit', { max: MAX_BADGES }) : ''}
        </p>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button type="button" variant="secondary" size="sm" onClick={selection.clear}>
          {t('selection.clear')}
        </Button>
        <BadgeSheet
          batch={{ kind: 'selection', arquebusierIds: selection.rows.map((row) => row.id) }}
          rows={selection.rows}
          onUnknownIds={selection.remove}
          disabled={tooMany}
          disabledReasonId={reasonId}
        />
      </div>
    </section>
  );
}

/** The counters, the filter bar and the table (or "nothing matches") of the list template. */
function ArquebusierList({ rows, loaded, loading, comparsas, filters, selection }: ListProps) {
  const { t } = useTranslation(['registry', 'ui']);
  const { columns, mobileRow } = useArquebusierColumns();
  const { status, license, warning, searchTerm } = filters;
  const shown = useMemo(
    () => filterRows(rows, { status, license, warning, searchTerm }),
    [rows, status, license, warning, searchTerm],
  );
  const resultText = useSettled(
    loaded && filters.filtered ? t('arquebusiers.resultCount', { count: shown.length }) : '',
  );

  return (
    <>
      {loaded && <StatFilter label={t('arquebusiers.counters.label')} items={filters.counters(rows)} />}
      <FilterBar
        filters={
          <>
            {comparsas.length > 1 && (
              <FilterSelect
                label={t('arquebusiers.filters.comparsa')}
                value={filters.comparsaId}
                onChange={(value) => {
                  filters.setFilter('comparsaId', value);
                }}
                options={[
                  { value: '', label: t('arquebusiers.filters.all') },
                  ...comparsas.map((comparsa) => ({ value: comparsa.id, label: comparsa.name })),
                ]}
              />
            )}
            <FilterSelect
              label={t('arquebusiers.filters.warning.label')}
              value={warning}
              onChange={(value) => {
                filters.setFilter('warning', value);
              }}
              options={[
                { value: '', label: t('arquebusiers.filters.warning.all') },
                ...WARNING_FILTERS.map((code) => ({
                  value: code,
                  label:
                    code === 'ANY' ? t('arquebusiers.filters.warning.any') : t(`ui:status.warning.${code}`),
                })),
              ]}
            />
          </>
        }
        search={
          <SearchField
            label={t('arquebusiers.search')}
            hint={t('arquebusiers.searchHint')}
            value={filters.term}
            onChange={filters.setTerm}
          />
        }
        resultText={resultText}
      />
      {selection && selection.rows.length > 0 && <SelectionBar selection={selection} />}
      {loaded && shown.length === 0 ? (
        <NoMatches title={t('arquebusiers.noMatches')} onClear={filters.clearFilters} />
      ) : (
        <DataTable
          caption={t('arquebusiers.caption')}
          data={shown}
          columns={columns}
          getRowId={(row) => row.id}
          getRowHref={(row) => `/arquebusiers/${row.id}`}
          mobileRow={mobileRow}
          isLoading={loading}
          emptyText={t('arquebusiers.noMatches')}
          {...selectionProps(selection)}
        />
      )}
    </>
  );
}

/**
 * Specs "Arquebusier visibility (BR-12)" and "Registry screens": the arquebusiers of the caller's
 * comparsas (every one for an Admin), filtered by comparsa through the API, by compliance warning,
 * and by status and license state with the counters above the list, which count the rows in scope (design D10). The
 * search looks at the loaded rows by name, DNI/NIE or federation id and stays out of the address,
 * because it may be a DNI.
 */
export function ArquebusiersPage() {
  const { t } = useTranslation('registry');
  useDocumentTitle(t('arquebusiers.title'));
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const signedIn = session.status === 'signedIn';

  const comparsas = useListComparsas({ includeInactive: true }, { query: { enabled: signedIn } });
  const comparsaList = useMemo(() => (comparsas.data?.data ?? []) as ComparsaResponse[], [comparsas.data]);
  const comparsaIds = useMemo(() => comparsaList.map((comparsa) => comparsa.id), [comparsaList]);
  const filters = useArquebusierFilters(comparsaIds);
  const { comparsaId } = filters;
  const params: ListArquebusiersParams = comparsaId ? { comparsaId } : {};
  // A comparsa filter in the address is only known once the comparsas are: until then nothing is
  // listed, rather than every arquebusier the caller may see.
  const waitingForComparsas = filters.comparsaInAddress && !comparsas.isSuccess;
  const arquebusiers = useListArquebusiers(params, {
    query: { enabled: signedIn && !waitingForComparsas },
  });
  const rows = useMemo(
    () => (arquebusiers.data?.data ?? []) as ArquebusierRowResponse[],
    [arquebusiers.data],
  );

  const selection = useBadgeSelection(rows, arquebusiers.isSuccess, comparsaId);
  const comparsaName = comparsaList.find((comparsa) => comparsa.id === comparsaId)?.name;
  // Spec "Badge screens": the whole comparsa when the list shows one and nothing is selected.
  const comparsaBadges = isAdmin &&
    comparsaId &&
    comparsaName &&
    rows.length > 0 &&
    selection.rows.length === 0 && (
      <BadgeSheet
        batch={{ kind: 'comparsa', comparsaId, comparsaName }}
        rows={rows}
        context={comparsaName}
        onUnknownIds={selection.remove}
      />
    );

  const [notice] = useNotice();
  const unassigned = !isAdmin && comparsas.isSuccess && comparsaList.length === 0;
  const lock = useRegistryLock();
  const moreActions = useRef<HTMLButtonElement>(null);
  const lockAction = useRegistryLockAction(lock, moreActions);
  // A locked registry takes no new arquebusiers from FiringChiefs (BR-10); Admins still register.
  const canRegister = lock.canWrite && (isAdmin || comparsaList.some((comparsa) => comparsa.active));
  const neverFilled = arquebusiers.isSuccess && rows.length === 0 && !comparsaId;
  const registerAction = canRegister && (
    <Button asChild>
      <Link to="/arquebusiers/new">
        <Plus aria-hidden="true" />
        {t('arquebusiers.new')}
      </Link>
    </Button>
  );
  // Spec "Import screen": the initial load from a spreadsheet, for Admins only (UC-09).
  const importAction = isAdmin && (
    <Button asChild variant="secondary">
      <Link to="/arquebusiers/import">
        <FileUp aria-hidden="true" />
        {t('import.action')}
      </Link>
    </Button>
  );
  const showList =
    !unassigned && !neverFilled && !arquebusiers.isError && !(waitingForComparsas && comparsas.isError);

  return (
    <>
      <PageHeader
        title={t('arquebusiers.title')}
        description={isAdmin ? t('arquebusiers.description') : t('arquebusiers.descriptionFiringChief')}
        moreActions={!unassigned && lockAction.item ? [lockAction.item] : []}
        moreActionsRef={moreActions}
        statuses={
          isAdmin && lock.known && <StatusBadge kind="registry" value={lock.locked ? 'LOCKED' : 'OPEN'} />
        }
        actions={
          !unassigned && (
            // The main action first, so it leads when the actions wrap on a phone (UI audit).
            <>
              {registerAction}
              {importAction}
              {comparsaBadges}
            </>
          )
        }
      />
      {lockAction.dialog}
      <NoticeBanner notice={notice} />
      <RegistryLockNotice lock={lock} />
      {comparsas.isError && (
        <LoadFailure
          error={comparsas.error}
          consequence={waitingForComparsas ? t('load.list') : undefined}
          onRetry={() => comparsas.refetch()}
        />
      )}
      {arquebusiers.isError && (
        <LoadFailure error={arquebusiers.error} onRetry={() => arquebusiers.refetch()} />
      )}
      {unassigned && (
        <EmptyState
          icon={IdCard}
          title={t('arquebusiers.unassigned.title')}
          description={t('arquebusiers.unassigned.description')}
        />
      )}
      {!unassigned && neverFilled && (
        <EmptyState
          icon={IdCard}
          title={t('arquebusiers.empty.title')}
          description={canRegister ? t('arquebusiers.empty.description') : undefined}
        />
      )}
      {showList && (
        <ArquebusierList
          rows={rows}
          loaded={arquebusiers.isSuccess}
          loading={arquebusiers.isPending}
          comparsas={comparsaList}
          filters={filters}
          selection={isAdmin ? selection : undefined}
        />
      )}
    </>
  );
}

/**
 * The badge selection, kept in this page only (it is cleared when the user leaves the list): the
 * selected rows survive filters and paging, take the latest data of rows still listed, and drop the
 * rows the registry no longer has — gone from the whole list, or from the comparsa being shown.
 */
function useBadgeSelection(
  rows: readonly ArquebusierRowResponse[],
  loaded: boolean,
  comparsaId: string,
): BadgeSelection {
  const [stored, setStored] = useState<ReadonlyMap<string, ArquebusierRowResponse>>(() => new Map());
  const listed = useMemo(() => new Map(rows.map((row) => [row.id, row])), [rows]);
  const selected = useMemo(
    () =>
      [...stored.values()]
        .map((row) => listed.get(row.id) ?? row)
        .filter(
          (row) => !loaded || listed.has(row.id) || (comparsaId !== '' && row.comparsaId !== comparsaId),
        ),
    [stored, listed, loaded, comparsaId],
  );
  return {
    rows: selected,
    change: (next) => {
      setStored((current) => {
        const kept = new Map<string, ArquebusierRowResponse>();
        for (const [id, isSelected] of Object.entries(next)) {
          if (!isSelected) continue;
          const row = listed.get(id) ?? current.get(id);
          if (row) kept.set(id, row);
        }
        return kept;
      });
    },
    remove: (ids) => {
      setStored((current) => new Map([...current].filter(([id]) => !ids.includes(id))));
    },
    clear: () => {
      setStored(new Map());
    },
  };
}
