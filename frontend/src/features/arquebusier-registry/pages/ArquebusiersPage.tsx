import { IdCard, Plus } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useListArquebusiers } from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import type { ArquebusierRowResponse, ComparsaResponse, ListArquebusiersParams } from '@/api/generated/model';
import { NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DataTable } from '@/components/app/DataTable';
import { EmptyState } from '@/components/app/EmptyState';
import { FilterBar, NoMatches } from '@/components/app/FilterBar';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { SearchField } from '@/components/app/SearchField';
import { StatFilter } from '@/components/app/StatFilter';
import { useSession } from '@/features/identity-access/session';
import { useNotice } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { LoadFailure } from '../components/LoadFailure';
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

interface ListProps {
  rows: readonly ArquebusierRowResponse[];
  loaded: boolean;
  loading: boolean;
  comparsas: readonly ComparsaResponse[];
  filters: ReturnType<typeof useArquebusierFilters>;
}

/** The counters, the filter bar and the table (or "nothing matches") of the list template. */
function ArquebusierList({ rows, loaded, loading, comparsas, filters }: ListProps) {
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

  const [notice] = useNotice();
  const unassigned = !isAdmin && comparsas.isSuccess && comparsaList.length === 0;
  const canRegister = isAdmin || comparsaList.some((comparsa) => comparsa.active);
  const neverFilled = arquebusiers.isSuccess && rows.length === 0 && !comparsaId;
  const registerAction = canRegister && (
    <Button asChild>
      <Link to="/arquebusiers/new">
        <Plus aria-hidden="true" />
        {t('arquebusiers.new')}
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
        actions={!unassigned && registerAction}
      />
      <NoticeBanner notice={notice} />
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
        />
      )}
    </>
  );
}
