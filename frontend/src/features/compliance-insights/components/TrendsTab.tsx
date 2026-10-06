import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useGetComplianceTrends } from '@/api/generated/compliance/compliance';
import type { TrendRowResponse, TrendsResponse } from '@/api/generated/model';
import { BarChart } from '@/components/app/BarChart';
import type { ChartPoint, ChartSeries } from '@/components/app/chart-data';
import { ChartFrame } from '@/components/app/ChartFrame';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { LineChart } from '@/components/app/LineChart';
import { SectionCard } from '@/components/app/SectionCard';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useFormatters } from '@/lib/format';
import { changeOf, editionsWithOrders, shareOf } from '../trends';

export interface TrendsTabProps {
  /** The comparsa of the shared filter, or `''` for every comparsa in the scope. */
  comparsaId: string;
  /** False until the scope is known, so no figures of a wider scope ever show. */
  enabled: boolean;
}

const KINDS = ['TRABUCO', 'ARCABUZ', 'PISTOL'] as const;

/** The year as the charts show it. */
const yearLabel = (row: TrendRowResponse): string => String(row.year);

function point(row: TrendRowResponse, values: Record<string, number | null>): ChartPoint {
  return { id: yearLabel(row), label: yearLabel(row), provisional: row.provisional, values };
}

/**
 * The summary sentences of the trends (spec: Trends screen): the latest edition against the
 * previous one, with "provisional" on an edition in progress.
 */
function useSummaries() {
  const { t } = useTranslation(['insights', 'ui']);
  const format = useFormatters();
  return useMemo(() => {
    const year = (row: TrendRowResponse) =>
      row.provisional ? t('ui:chart.provisionalLabel', { label: yearLabel(row) }) : yearLabel(row);
    const percent = (value: number) => format.number(value, { style: 'percent', maximumFractionDigits: 0 });
    // A change under 1 % keeps a decimal, so "0 % more" never contradicts different figures.
    const changePercent = (ratio: number) =>
      format.number(ratio, { style: 'percent', maximumFractionDigits: ratio < 0.01 ? 1 : 0 });
    /** `{{key}}.summary` with the change of `value`, or `{{key}}.summaryAlone` without a previous figure. */
    const counted = (
      key: 'arquebusiers' | 'firstYear' | 'powder' | 'caps',
      latest: TrendRowResponse,
      previous: TrendRowResponse | undefined,
      value: (row: TrendRowResponse) => number,
    ) => {
      const change = changeOf(value(latest), previous ? value(previous) : undefined);
      // Each sentence names the figure differently (active, count, value).
      const figure = format.number(value(latest));
      const values = { year: year(latest), active: figure, count: figure, value: figure };
      if (change.kind === 'alone' || !previous) return t(`insights:trends.${key}.summaryAlone`, values);
      const changeText =
        change.kind === 'same'
          ? t('insights:trends.change.same', { previous: yearLabel(previous) })
          : t(`insights:trends.change.${change.kind}`, {
              change: changePercent(change.ratio),
              previous: yearLabel(previous),
            });
      return t(`insights:trends.${key}.summary`, { ...values, change: changeText });
    };
    /** `{{key}}.summary` with the latest and previous shares. */
    const shared = (
      key: 'women' | 'weapons',
      latest: TrendRowResponse,
      previous: TrendRowResponse | undefined,
      share: (row: TrendRowResponse) => number,
    ) =>
      previous
        ? t(`insights:trends.${key}.summary`, {
            year: year(latest),
            share: percent(share(latest)),
            previousShare: percent(share(previous)),
            previous: yearLabel(previous),
          })
        : t(`insights:trends.${key}.summaryAlone`, { year: year(latest), share: percent(share(latest)) });
    return { year, percent, counted, shared };
  }, [t, format]);
}

const womenShare = (row: TrendRowResponse) => shareOf(row.gender.female, row.active);
const ownedShare = (row: TrendRowResponse) => shareOf(row.weaponSources.owned, row.active);

/** The six views of the trends, for at least two editions with orders (spec: Trends screen). */
function TrendCharts({
  rows,
  comparsas,
}: {
  rows: TrendRowResponse[];
  comparsas: TrendsResponse['comparsas'];
}) {
  const { t } = useTranslation(['insights', 'catalog']);
  const format = useFormatters();
  const summaries = useSummaries();
  const latest = rows[rows.length - 1];
  const previous = rows[rows.length - 2];

  const charts = useMemo(() => {
    const count = (value: number) => format.number(value);
    const percent = summaries.percent;
    const firstYearRows = rows.filter((row) => row.firstYear !== null);
    return {
      arquebusiers: {
        series: [
          { key: 'active', label: t('statistics.figures.active') },
          { key: 'reserve', label: t('statistics.figures.reserve') },
        ] satisfies ChartSeries[],
        data: rows.map((row) => point(row, { active: row.active, reserve: row.reserve })),
      },
      women: {
        series: [{ key: 'women', label: t('trends.women.series') }] satisfies ChartSeries[],
        tableSeries: [
          { key: 'unknown', label: t('trends.women.unknown'), formatValue: count },
        ] satisfies ChartSeries[],
        data: rows.map((row) => point(row, { women: womenShare(row), unknown: row.gender.unknown })),
        formatValue: percent,
      },
      firstYear: {
        series: [{ key: 'firstYear', label: t('trends.firstYear.series') }] satisfies ChartSeries[],
        data: firstYearRows.map((row) => point(row, { firstYear: row.firstYear })),
        rows: firstYearRows,
      },
      powder: {
        series: [{ key: 'powder', label: t('trends.powder.series') }] satisfies ChartSeries[],
        data: rows.map((row) => point(row, { powder: row.powderKg })),
      },
      caps: {
        series: [{ key: 'caps', label: t('trends.caps.series') }] satisfies ChartSeries[],
        data: rows.map((row) => point(row, { caps: row.capsBoxes })),
      },
      weapons: {
        series: (['owned', 'rental', 'loan', 'none'] as const).map((key) => ({
          key,
          label: t(`trends.weapons.${key}`),
        })),
        data: rows.map((row) => point(row, { ...row.weaponSources })),
      },
      rentals: {
        series: [
          ...KINDS.map((kind) => ({ key: kind, label: t(`catalog:kind.${kind}`) })),
          { key: 'flasks', label: t('trends.rentals.flasks') },
        ] satisfies ChartSeries[],
        data: rows.map((row) =>
          point(row, {
            ...Object.fromEntries(row.rentalsByKind.map((rental) => [rental.kind, rental.count])),
            flasks: row.flaskRentals,
          }),
        ),
      },
    };
  }, [rows, t, format, summaries.percent]);

  if (!latest) return null;
  const rented = (row: TrendRowResponse) => row.rentalsByKind.reduce((sum, rental) => sum + rental.count, 0);
  const firstYearLatest = charts.firstYear.rows[charts.firstYear.rows.length - 1];
  const firstYearPrevious = charts.firstYear.rows[charts.firstYear.rows.length - 2];
  const firstYearFrame = {
    title: t('trends.firstYear.title'),
    summary: firstYearLatest
      ? summaries.counted('firstYear', firstYearLatest, firstYearPrevious, (row) => row.firstYear ?? 0)
      : '',
    note: charts.firstYear.rows.length < rows.length ? t('trends.firstYear.note') : undefined,
    categoryLabel: t('trends.edition'),
    series: charts.firstYear.series,
    data: charts.firstYear.data,
  };

  return (
    <div className="flex flex-col gap-section">
      <BarChart
        layout="stacked"
        title={t('trends.arquebusiers.title')}
        summary={summaries.counted('arquebusiers', latest, previous, (row) => row.active)}
        categoryLabel={t('trends.edition')}
        series={charts.arquebusiers.series}
        data={charts.arquebusiers.data}
      />
      <LineChart
        title={t('trends.women.title')}
        summary={summaries.shared('women', latest, previous, womenShare)}
        note={
          latest.gender.unknown > 0
            ? t('trends.women.note', { count: latest.gender.unknown, year: yearLabel(latest) })
            : t('trends.women.noteNone')
        }
        categoryLabel={t('trends.edition')}
        series={charts.women.series}
        tableSeries={charts.women.tableSeries}
        data={charts.women.data}
        formatValue={charts.women.formatValue}
      />
      {charts.firstYear.rows.length >= 2 ? (
        <BarChart {...firstYearFrame} />
      ) : (
        charts.firstYear.rows.length === 1 && <ChartFrame {...firstYearFrame} />
      )}
      <div className="grid gap-section lg:grid-cols-2">
        <BarChart
          title={t('trends.powder.title')}
          summary={summaries.counted('powder', latest, previous, (row) => row.powderKg)}
          categoryLabel={t('trends.edition')}
          series={charts.powder.series}
          data={charts.powder.data}
        />
        <BarChart
          title={t('trends.caps.title')}
          summary={summaries.counted('caps', latest, previous, (row) => row.capsBoxes)}
          categoryLabel={t('trends.edition')}
          series={charts.caps.series}
          data={charts.caps.data}
        />
      </div>
      <BarChart
        layout="percent"
        title={t('trends.weapons.title')}
        summary={summaries.shared('weapons', latest, previous, ownedShare)}
        categoryLabel={t('trends.edition')}
        series={charts.weapons.series}
        data={charts.weapons.data}
      />
      <BarChart
        title={t('trends.rentals.title')}
        summary={
          previous
            ? t('trends.rentals.summary', {
                year: summaries.year(latest),
                weapons: format.number(rented(latest)),
                previousWeapons: format.number(rented(previous)),
                previous: yearLabel(previous),
                flasks: format.number(latest.flaskRentals),
              })
            : t('trends.rentals.summaryAlone', {
                year: summaries.year(latest),
                weapons: format.number(rented(latest)),
                flasks: format.number(latest.flaskRentals),
              })
        }
        categoryLabel={t('trends.edition')}
        series={charts.rentals.series}
        data={charts.rentals.data}
      />
      {comparsas.length > 0 && <ComparsaTrends rows={rows} comparsas={comparsas} />}
    </div>
  );
}

/** A change in figures as signed digits for the eye and as words for screen readers ("down 2"). */
function ChangeText({ change }: { change: number }) {
  const { t } = useTranslation('insights');
  const { number } = useFormatters();
  return (
    <>
      <span aria-hidden="true">{number(change, { signDisplay: 'exceptZero' })}</span>
      <span className="sr-only">{changeLabel(t, number, change)}</span>
    </>
  );
}

function changeLabel(
  t: ReturnType<typeof useTranslation<'insights'>>['t'],
  number: ReturnType<typeof useFormatters>['number'],
  change: number,
): string {
  if (change === 0) return t('trends.comparsas.noChange');
  const count = number(Math.abs(change));
  return change > 0 ? t('trends.comparsas.up', { count }) : t('trends.comparsas.down', { count });
}

interface ComparsaTrendRow {
  id: string;
  name: string;
  /** Active entries per edition, in the order of the editions. */
  counts: number[];
  change: number;
}

/** Active arquebusiers per comparsa and edition, with the change in the latest one (spec: Trends screen). */
function ComparsaTrends({
  rows,
  comparsas,
}: {
  rows: TrendRowResponse[];
  comparsas: TrendsResponse['comparsas'];
}) {
  const { t } = useTranslation(['insights', 'ui']);
  const { number, list } = useFormatters();
  const latest = rows[rows.length - 1];
  const previous = rows[rows.length - 2];

  const data = useMemo<ComparsaTrendRow[]>(
    () =>
      comparsas.map((comparsa) => {
        const counts = rows.map(
          (row) => row.comparsas.find((entry) => entry.comparsaId === comparsa.id)?.active ?? 0,
        );
        return {
          id: comparsa.id,
          name: comparsa.name,
          counts,
          change: (counts[counts.length - 1] ?? 0) - (counts[counts.length - 2] ?? 0),
        };
      }),
    [rows, comparsas],
  );
  const columns = useMemo<DataTableColumn<ComparsaTrendRow>[]>(
    () => [
      {
        id: 'name',
        header: t('trends.comparsas.comparsa'),
        rowHeader: true,
        sortValue: (row) => row.name,
        cell: (row) => (
          <Link to={`/comparsas/${row.id}`} className="font-semibold text-foreground hover:underline">
            {row.name}
          </Link>
        ),
      },
      ...rows.map((edition, index): DataTableColumn<ComparsaTrendRow> => ({
        id: yearLabel(edition),
        header: edition.provisional
          ? t('ui:chart.provisionalLabel', { label: yearLabel(edition) })
          : yearLabel(edition),
        align: 'end',
        sortValue: (row) => row.counts[index] ?? 0,
        cell: (row) => number(row.counts[index] ?? 0),
      })),
      {
        id: 'change',
        header: t('trends.comparsas.change', { previous: previous ? yearLabel(previous) : '' }),
        align: 'end',
        sortValue: (row) => row.change,
        cell: (row) => <ChangeText change={row.change} />,
      },
    ],
    [rows, previous, t, number],
  );

  return (
    <SectionCard title={t('trends.comparsas.title')} span="full">
      <p className="text-body text-foreground">
        {t('trends.comparsas.summary', { year: latest ? yearLabel(latest) : '' })}
      </p>
      <DataTable
        caption={t('trends.comparsas.caption')}
        data={data}
        columns={columns}
        getRowId={(row) => row.id}
        getRowHref={(row) => `/comparsas/${row.id}`}
        mobileRow={(row) => (
          <>
            <Link to={`/comparsas/${row.id}`} className="font-semibold text-foreground">
              {row.name}
            </Link>
            <span className="text-help text-muted-foreground">
              {list(
                rows.map((edition, index) =>
                  t('trends.comparsas.yearCount', {
                    year: edition.provisional
                      ? t('ui:chart.provisionalLabel', { label: yearLabel(edition) })
                      : yearLabel(edition),
                    count: number(row.counts[index] ?? 0),
                  }),
                ),
              )}
            </span>
            <span className="text-help text-muted-foreground">
              {t('trends.comparsas.changeText', { previous: previous ? yearLabel(previous) : '' })}{' '}
              <ChangeText change={row.change} />
            </span>
          </>
        )}
        paginated={false}
      />
    </SectionCard>
  );
}

/**
 * One edition with orders: its figures as a table of measures, without charts (spec: Trends screen,
 * Only one edition), so it reads on a phone too.
 */
function SingleEdition({ row }: { row: TrendRowResponse }) {
  const { t } = useTranslation(['insights', 'ui']);
  const year = row.provisional ? t('ui:chart.provisionalLabel', { label: yearLabel(row) }) : yearLabel(row);
  const series = useMemo<ChartSeries[]>(() => [{ key: 'value', label: year }], [year]);
  const data = useMemo<ChartPoint[]>(() => {
    const measures: [string, number][] = [
      [t('statistics.figures.active'), row.active],
      [t('statistics.figures.reserve'), row.reserve],
      [t('trends.women.series'), row.gender.female],
      [t('trends.women.unknown'), row.gender.unknown],
      [t('trends.powder.series'), row.powderKg],
      [t('trends.caps.series'), row.capsBoxes],
      [t('trends.weapons.owned'), row.weaponSources.owned],
      [t('trends.weapons.rental'), row.weaponSources.rental],
      [t('trends.weapons.loan'), row.weaponSources.loan],
      [t('trends.weapons.none'), row.weaponSources.none],
      [t('trends.rentals.flasks'), row.flaskRentals],
    ];
    return measures.map(([label, value]) => ({ id: label, label, values: { value } }));
  }, [row, t]);
  return (
    <ChartFrame
      title={t('trends.single.title', { year })}
      summary={t('trends.fewEditions', { year })}
      categoryLabel={t('trends.single.measure')}
      series={series}
      data={data}
    />
  );
}

/**
 * The "Trends" tab of the Statistics page (specs: Trends screen, Edition trends (UC-07)): the editions
 * with orders in the user's scope, as charts with their tables. Lazy-loaded with Recharts.
 */
export default function TrendsTab({ comparsaId, enabled }: TrendsTabProps) {
  const { t } = useTranslation('insights');
  const query = useGetComplianceTrends(comparsaId ? { comparsaId } : {}, {
    // No placeholder: figures of a wider scope never stay on screen while a comparsa's load.
    query: { enabled },
  });
  const trends = query.isSuccess ? (query.data.data as TrendsResponse) : undefined;
  const rows = useMemo(() => (trends ? editionsWithOrders(trends.rows) : []), [trends]);

  const loading = enabled && !trends && !query.isError;
  const [only] = rows;
  const provisional = rows.find((row) => row.provisional);
  return (
    <div className="flex flex-col gap-section" aria-busy={query.isFetching || undefined}>
      {/* One status region for the whole tab, so the loading text is announced when it changes. */}
      <p role="status" className={loading ? 'text-muted-foreground' : 'sr-only'}>
        {loading ? t('trends.loading') : ''}
      </p>
      {query.isError && <LoadFailure error={query.error} onRetry={() => query.refetch()} />}
      {trends && provisional && (
        <p className="text-help text-muted-foreground">
          {t('trends.provisionalNote', { year: String(provisional.year) })}
        </p>
      )}
      {trends &&
        (rows.length >= 2 ? (
          <TrendCharts rows={rows} comparsas={trends.comparsas} />
        ) : only ? (
          <SingleEdition row={only} />
        ) : (
          <p className="text-body text-muted-foreground">{t('trends.noEditions')}</p>
        ))}
    </div>
  );
}
