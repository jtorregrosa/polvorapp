import { keepPreviousData } from '@tanstack/react-query';
import { IdCard } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import { useGetComplianceStatistics } from '@/api/generated/compliance/compliance';
import {
  ArquebusierStatus,
  type ComparsaResponse,
  type ComparsaStatisticsResponse,
  type ComplianceStatisticsResponse,
  type GenderCounts,
  type GetComplianceStatisticsParams,
} from '@/api/generated/model';
import { Breakdown } from '@/components/app/Breakdown';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { EmptyState } from '@/components/app/EmptyState';
import { FilterBar, NoMatches } from '@/components/app/FilterBar';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { SectionCard } from '@/components/app/SectionCard';
import { SectionGrid } from '@/components/app/SectionGrid';
import { StatCard } from '@/components/app/StatCard';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useSession } from '@/features/identity-access/session';
import { useFormatters } from '@/lib/format';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

const STATUSES = Object.values(ArquebusierStatus);
type CountColumn = 'total' | 'active' | 'reserve' | 'female' | 'male' | 'unspecified' | 'withWarnings';
/** Women first, as in the Federation's equality reports. */
const GENDERS = ['female', 'male', 'unspecified'] as const satisfies readonly (keyof GenderCounts)[];

const countsOf = (counts: GenderCounts): number[] => GENDERS.map((gender) => counts[gender]);

/** The breakdowns of the statistics, each in a section of the grid (spec: Statistics screen). */
function Report({ statistics }: { statistics: ComplianceStatisticsResponse }) {
  const { t } = useTranslation(['insights', 'catalog']);
  const genderColumns = GENDERS.map((gender) => ({ id: gender, label: t(`statistics.gender.${gender}`) }));
  const { total, licenses, ownedWeapons } = statistics;
  const weapons = ownedWeapons.byKind.reduce((sum, kind) => sum + kind.count, 0);

  return (
    <SectionGrid>
      <SectionCard title={t('statistics.gender.title')}>
        <Breakdown
          title={t('statistics.gender.table')}
          categoryLabel={t('statistics.gender.category')}
          columns={[{ id: 'count', label: t('statistics.figures.total') }]}
          rows={GENDERS.map((gender) => ({
            id: gender,
            label: t(`statistics.gender.${gender}`),
            counts: [statistics.gender[gender]],
          }))}
          total={total}
        />
      </SectionCard>
      <SectionCard title={t('statistics.ageBrackets.title')}>
        <Breakdown
          title={t('statistics.ageBrackets.table')}
          categoryLabel={t('statistics.ageBrackets.category')}
          columns={genderColumns}
          rows={statistics.ageBrackets.map(({ bracket, counts }) => ({
            id: bracket,
            label: t(`statistics.ageBrackets.${bracket}`),
            counts: countsOf(counts),
          }))}
          total={total}
          showRowTotal
        />
      </SectionCard>
      <SectionCard title={t('statistics.course.title')}>
        <Breakdown
          title={t('statistics.course.table')}
          categoryLabel={t('statistics.course.category')}
          columns={genderColumns}
          rows={[
            { id: 'done', label: t('statistics.course.done'), counts: countsOf(statistics.course.done) },
            {
              id: 'notDone',
              label: t('statistics.course.notDone'),
              counts: countsOf(statistics.course.notDone),
            },
          ]}
          total={total}
          showRowTotal
        />
      </SectionCard>
      <SectionCard title={t('statistics.licenses.title')}>
        <Breakdown
          title={t('statistics.licenses.table')}
          categoryLabel={t('statistics.licenses.category')}
          columns={[{ id: 'count', label: t('statistics.figures.total') }]}
          rows={(['valid', 'expiring', 'expired', 'pending', 'none'] as const).map((state) => ({
            id: state,
            label: t(`statistics.licenses.${state}`),
            counts: [licenses[state]],
          }))}
          total={total}
        />
      </SectionCard>
      <SectionCard title={t('statistics.ownedWeapons.title')} span="full">
        <div className="grid gap-group lg:grid-cols-2">
          <Breakdown
            title={t('statistics.ownedWeapons.table')}
            categoryLabel={t('statistics.ownedWeapons.category')}
            columns={genderColumns}
            rows={[
              {
                id: 'with',
                label: t('statistics.ownedWeapons.withWeapon'),
                counts: countsOf(ownedWeapons.withWeapon),
              },
              {
                id: 'without',
                label: t('statistics.ownedWeapons.withoutWeapon'),
                counts: countsOf(ownedWeapons.withoutWeapon),
              },
            ]}
            total={total}
            showRowTotal
          />
          <Breakdown
            title={t('statistics.ownedWeapons.byKindTable')}
            categoryLabel={t('statistics.ownedWeapons.kindCategory')}
            columns={[{ id: 'count', label: t('statistics.ownedWeapons.weapons') }]}
            rows={ownedWeapons.byKind.map(({ kind, count }) => ({
              id: kind,
              label: t(`catalog:kind.${kind}`),
              counts: [count],
            }))}
            total={weapons}
            shareLabel={t('statistics.ownedWeapons.shareOfWeapons')}
          />
        </div>
      </SectionCard>
    </SectionGrid>
  );
}

/** The figures of each comparsa, each name linking to the comparsa. */
function ComparsaTable({ rows }: { rows: readonly ComparsaStatisticsResponse[] }) {
  const { t } = useTranslation('insights');
  const { number } = useFormatters();
  const columns = useMemo<DataTableColumn<ComparsaStatisticsResponse>[]>(() => {
    const count = (
      id: CountColumn,
      value: (row: ComparsaStatisticsResponse) => number,
    ): DataTableColumn<ComparsaStatisticsResponse> => ({
      id,
      header: t(`statistics.comparsas.columns.${id}`),
      align: 'end',
      sortValue: value,
      cell: (row) => number(value(row)),
    });
    return [
      {
        id: 'name',
        header: t('statistics.comparsas.columns.name'),
        sortValue: (row) => row.name,
        cell: (row) => (
          <Link to={`/comparsas/${row.comparsaId}`} className="font-semibold text-foreground hover:underline">
            {row.name}
          </Link>
        ),
      },
      count('total', (row) => row.total),
      count('active', (row) => row.active),
      count('reserve', (row) => row.reserve),
      count('female', (row) => row.gender.female),
      count('male', (row) => row.gender.male),
      count('unspecified', (row) => row.gender.unspecified),
      count('withWarnings', (row) => row.withWarnings),
    ];
  }, [t, number]);

  return (
    <SectionCard title={t('statistics.comparsas.title')} span="full">
      <DataTable
        caption={t('statistics.comparsas.caption')}
        data={rows}
        columns={columns}
        getRowId={(row) => row.comparsaId}
        getRowHref={(row) => `/comparsas/${row.comparsaId}`}
        mobileRow={(row) => (
          <>
            <Link to={`/comparsas/${row.comparsaId}`} className="font-semibold text-foreground">
              {row.name}
            </Link>
            <span className="text-help text-muted-foreground">
              {t('statistics.comparsas.summary', {
                total: number(row.total),
                active: number(row.active),
                reserve: number(row.reserve),
                withWarnings: number(row.withWarnings),
              })}
            </span>
            <span className="text-help text-muted-foreground">
              {t('statistics.comparsas.genderSummary', {
                female: number(row.gender.female),
                male: number(row.gender.male),
                unspecified: number(row.gender.unspecified),
              })}
            </span>
          </>
        )}
        paginated={false}
        emptyText={t('statistics.noMatches')}
      />
    </SectionCard>
  );
}

/**
 * Specs "Statistics (UC-07)" and "Statistics screen": the aggregates of the user's scope, filtered
 * by comparsa and status in the address, with the equality report (dashboard template). Only
 * counts reach the page; there is no download (exports belong to add-exports).
 */
export function StatisticsPage() {
  const { t } = useTranslation('insights');
  const { t: tRegistry } = useTranslation('registry');
  const { number } = useFormatters();
  useDocumentTitle(t('statistics.title'));
  const session = useSession();
  const signedIn = session.status === 'signedIn';
  const isAdmin = session.account?.role === 'ADMIN';
  const [search, setSearch] = useSearchParams();

  const comparsas = useListComparsas({ includeInactive: true }, { query: { enabled: signedIn } });
  const comparsaList = useMemo(() => (comparsas.data?.data ?? []) as ComparsaResponse[], [comparsas.data]);
  const comparsaId = knownFilter(
    search.get('comparsaId'),
    comparsaList.map((comparsa) => comparsa.id),
  );
  const status = knownFilter(search.get('status'), STATUSES);
  const filtered = comparsaId !== '' || status !== '';
  // A FiringChief's scope is only known once their comparsas are: nothing is shown before, so a
  // FiringChief without comparsas never sees figures flash.
  const scopeKnown = isAdmin || !comparsas.isPending;
  const unassigned = !isAdmin && comparsas.isSuccess && comparsaList.length === 0;
  // A comparsa in the address is only known once the comparsas are.
  const waitingForComparsas = search.get('comparsaId') !== null && !comparsas.isSuccess;

  const params: GetComplianceStatisticsParams = {
    ...(comparsaId ? { comparsaId } : {}),
    ...(status ? { status } : {}),
  };
  // The previous figures stay while new filters load, so the page neither empties nor jumps.
  const query = useGetComplianceStatistics(params, {
    query: {
      enabled: signedIn && scopeKnown && !unassigned && !waitingForComparsas,
      placeholderData: keepPreviousData,
    },
  });
  const statistics = query.isSuccess ? (query.data.data as ComplianceStatisticsResponse) : undefined;

  const setFilter = (key: 'comparsaId' | 'status', value: string): void => {
    setSearch(withFilter(search, key, value), { replace: true });
  };
  const noMatches = statistics?.total === 0 && filtered;

  return (
    <>
      <PageHeader title={t('statistics.title')} description={t('statistics.description')} />
      {!scopeKnown ? null : unassigned ? (
        <EmptyState
          icon={IdCard}
          title={tRegistry('arquebusiers.unassigned.title')}
          description={tRegistry('arquebusiers.unassigned.description')}
        />
      ) : (
        <>
          <FilterBar
            filters={
              <>
                {comparsaList.length > 1 && (
                  <FilterSelect
                    label={t('statistics.filters.comparsa')}
                    value={comparsaId}
                    onChange={(value) => {
                      setFilter('comparsaId', value);
                    }}
                    options={[
                      { value: '', label: t('statistics.filters.allComparsas') },
                      ...comparsaList.map((comparsa) => ({ value: comparsa.id, label: comparsa.name })),
                    ]}
                  />
                )}
                <FilterSelect
                  label={t('statistics.filters.status')}
                  value={status}
                  onChange={(value) => {
                    setFilter('status', value);
                  }}
                  options={[
                    { value: '', label: t('statistics.filters.allStatuses') },
                    ...STATUSES.map((value) => ({ value, label: t(`statistics.filters.${value}`) })),
                  ]}
                />
              </>
            }
            // Announced whenever the figures change, filtered or not, once they have loaded.
            resultText={
              statistics && !query.isPlaceholderData
                ? t('statistics.resultCount', { count: statistics.total })
                : ''
            }
          />
          {comparsas.isError && (
            <LoadFailure
              error={comparsas.error}
              consequence={waitingForComparsas ? tRegistry('load.list') : undefined}
              onRetry={() => comparsas.refetch()}
            />
          )}
          {query.isError && <LoadFailure error={query.error} onRetry={() => query.refetch()} />}
          {noMatches && (
            <NoMatches
              title={t('statistics.noMatches')}
              onClear={() => {
                setSearch(new URLSearchParams(), { replace: true });
              }}
            />
          )}
          {statistics && !noMatches && (
            <div className="flex flex-col gap-section" aria-busy={query.isFetching || undefined}>
              <ul className="grid gap-3 sm:grid-cols-3">
                <li className="flex">
                  <StatCard
                    className="w-full"
                    label={t('statistics.figures.total')}
                    value={number(statistics.total)}
                  />
                </li>
                <li className="flex">
                  <StatCard
                    className="w-full"
                    label={t('statistics.figures.active')}
                    value={number(statistics.active)}
                  />
                </li>
                <li className="flex">
                  <StatCard
                    className="w-full"
                    label={t('statistics.figures.reserve')}
                    value={number(statistics.reserve)}
                  />
                </li>
              </ul>
              <Report statistics={statistics} />
              {statistics.comparsas.length > 0 && <ComparsaTable rows={statistics.comparsas} />}
            </div>
          )}
        </>
      )}
    </>
  );
}
