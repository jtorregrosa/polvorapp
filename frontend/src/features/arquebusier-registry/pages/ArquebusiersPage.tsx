import { IdCard, Plus } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useListArquebusiers } from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import {
  ArquebusierStatus,
  type ArquebusierRowResponse,
  type ComparsaResponse,
  type ListArquebusiersParams,
} from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { EmptyState } from '@/components/app/EmptyState';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { SearchField } from '@/components/app/SearchField';
import { StatusBadge } from '@/components/app/StatusBadge';
import { useSession } from '@/features/identity-access/session';
import { useNotice } from '@/lib/notices';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { LoadFailure } from '../components/LoadFailure';
import { matchesSearch, searchKey } from '../search';

const STATUSES = Object.values(ArquebusierStatus);

/** Pause after the last keystroke before the count is announced, so it is not read per letter. */
const ANNOUNCE_DELAY_MS = 400;

/** The text for the result-count live region: `text`, shortly after it settles, once the user has filtered. */
function useResultCount(filtered: { readonly current: boolean }, text: string): string {
  const [announced, setAnnounced] = useState('');
  useEffect(() => {
    if (!filtered.current || text === '') return undefined;
    const timer = setTimeout(() => {
      setAnnounced(text);
    }, ANNOUNCE_DELAY_MS);
    return () => {
      clearTimeout(timer);
    };
  }, [filtered, text]);
  return announced;
}

/**
 * Specs "Arquebusier visibility (BR-12)" and "Registry screens": the arquebusiers of the caller's
 * comparsas (every one for an Admin), filtered by comparsa and status through the API, and searched
 * in the loaded rows by name, DNI/NIE or federation id. The search term stays out of the address,
 * because it may be a DNI.
 */
export function ArquebusiersPage() {
  const { t } = useTranslation('registry');
  useDocumentTitle(t('arquebusiers.title'));
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const signedIn = session.status === 'signedIn';
  const [search, setSearch] = useSearchParams();
  const [term, setTerm] = useState('');
  // Counts are announced once the user has searched or filtered, not on the first load.
  const filtered = useRef(false);

  const comparsas = useListComparsas({ includeInactive: true }, { query: { enabled: signedIn } });
  const comparsaList = useMemo(() => (comparsas.data?.data ?? []) as ComparsaResponse[], [comparsas.data]);
  const comparsaId = knownFilter(
    search.get('comparsaId'),
    comparsaList.map((comparsa) => comparsa.id),
  );
  const status = knownFilter(search.get('status'), STATUSES);
  const params: ListArquebusiersParams = {
    ...(comparsaId ? { comparsaId } : {}),
    ...(status ? { status } : {}),
  };
  // A comparsa filter in the address is only known once the comparsas are: until then nothing is
  // listed, rather than every arquebusier the caller may see.
  const waitingForComparsas = search.get('comparsaId') !== null && !comparsas.isSuccess;
  const arquebusiers = useListArquebusiers(params, {
    query: { enabled: signedIn && !waitingForComparsas },
  });
  const rows = useMemo(
    () => (arquebusiers.data?.data ?? []) as ArquebusierRowResponse[],
    [arquebusiers.data],
  );

  const searchTerm = searchKey(term);
  const shown = useMemo(
    () => (searchTerm ? rows.filter((row) => matchesSearch(row, searchTerm)) : rows),
    [rows, searchTerm],
  );

  const columns = useMemo<DataTableColumn<ArquebusierRowResponse>[]>(
    () => [
      {
        id: 'name',
        header: t('arquebusiers.columns.name'),
        sortValue: (row) => `${row.lastName} ${row.firstName}`,
        cell: (row) => (
          <Link
            to={`/arquebusiers/${row.id}`}
            className="font-medium text-primary underline-offset-4 hover:underline"
          >
            {`${row.lastName}, ${row.firstName}`}
          </Link>
        ),
      },
      { id: 'nationalId', header: t('arquebusiers.columns.nationalId'), cell: (row) => row.nationalId },
      {
        id: 'federationId',
        header: t('arquebusiers.columns.federationId'),
        align: 'end',
        sortValue: (row) => row.federationId,
        cell: (row) => row.federationId,
      },
      {
        id: 'comparsa',
        header: t('arquebusiers.columns.comparsa'),
        sortValue: (row) => row.comparsaName,
        cell: (row) => row.comparsaName,
      },
      {
        id: 'status',
        header: t('arquebusiers.columns.status'),
        cell: (row) => <StatusBadge kind="arquebusier" value={row.status} />,
      },
      {
        id: 'license',
        header: t('arquebusiers.columns.license'),
        cell: (row) =>
          row.licenseStatus ? (
            <StatusBadge kind="license" value={row.licenseStatus} />
          ) : (
            <span className="text-sm text-muted-foreground">{t('arquebusiers.noLicense')}</span>
          ),
      },
    ],
    [t],
  );

  const setFilter = (key: 'comparsaId' | 'status', value: string): void => {
    filtered.current = true;
    setSearch(withFilter(search, key, value), { replace: true });
  };

  const [notice] = useNotice();
  const resultCount = useResultCount(
    filtered,
    arquebusiers.isSuccess
      ? shown.length === 0
        ? t('arquebusiers.noMatches')
        : t('arquebusiers.resultCount', { count: shown.length })
      : '',
  );
  const unassigned = !isAdmin && comparsas.isSuccess && comparsaList.length === 0;
  const canRegister = isAdmin || comparsaList.some((comparsa) => comparsa.active);
  const neverFilled = arquebusiers.isSuccess && rows.length === 0 && !comparsaId && !status;
  const registerAction = canRegister && (
    <Button asChild>
      <Link to="/arquebusiers/new">
        <Plus aria-hidden="true" />
        {t('arquebusiers.new')}
      </Link>
    </Button>
  );

  return (
    <>
      <PageHeader
        title={t('arquebusiers.title')}
        description={isAdmin ? t('arquebusiers.description') : t('arquebusiers.descriptionFiringChief')}
        actions={!unassigned && registerAction}
      />
      {notice && (
        <AlertBanner key={notice.id} severity={notice.severity} className="mb-4 max-w-xl" focusOnMount>
          {notice.text}
        </AlertBanner>
      )}
      {comparsas.isError && (
        <LoadFailure
          className="mb-4"
          error={comparsas.error}
          consequence={waitingForComparsas ? t('load.list') : undefined}
          onRetry={() => comparsas.refetch()}
        />
      )}
      {arquebusiers.isError && (
        <LoadFailure className="mb-4" error={arquebusiers.error} onRetry={() => arquebusiers.refetch()} />
      )}
      {/* Searching and filtering change the table silently: say how many rows remain (WCAG 4.1.3). */}
      <p role="status" className="sr-only">
        {resultCount}
      </p>
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
      {!unassigned &&
        !neverFilled &&
        !arquebusiers.isError &&
        !(waitingForComparsas && comparsas.isError) && (
          <>
            <div className="mb-4 flex flex-wrap items-start gap-4">
              <SearchField
                label={t('arquebusiers.search')}
                hint={t('arquebusiers.searchHint')}
                value={term}
                onChange={(value) => {
                  filtered.current = true;
                  setTerm(value);
                }}
              />
              {comparsaList.length > 1 && (
                <FilterSelect
                  label={t('arquebusiers.filters.comparsa')}
                  value={comparsaId}
                  onChange={(value) => {
                    setFilter('comparsaId', value);
                  }}
                  options={[
                    { value: '', label: t('arquebusiers.filters.all') },
                    ...comparsaList.map((comparsa) => ({ value: comparsa.id, label: comparsa.name })),
                  ]}
                />
              )}
              <FilterSelect
                label={t('arquebusiers.filters.status')}
                value={status}
                onChange={(value) => {
                  setFilter('status', value);
                }}
                options={[
                  { value: '', label: t('arquebusiers.filters.allStatuses') },
                  ...STATUSES.map((value) => ({
                    value,
                    label: t(`status.arquebusier.${value}`, { ns: 'ui' }),
                  })),
                ]}
              />
            </div>
            <DataTable
              caption={t('arquebusiers.caption')}
              data={shown}
              columns={columns}
              getRowId={(row) => row.id}
              isLoading={arquebusiers.isPending}
              emptyText={t('arquebusiers.noMatches')}
            />
          </>
        )}
    </>
  );
}
