import { IdCard } from 'lucide-react';
import { useId, useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useListArquebusiers } from '@/api/generated/arquebusiers/arquebusiers';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import { useGetComplianceSummary } from '@/api/generated/compliance/compliance';
import {
  ComplianceWarning,
  type ArquebusierRowResponse,
  type ComparsaResponse,
  type ComplianceSummaryResponse,
} from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { EmptyState } from '@/components/app/EmptyState';
import { PageHeader } from '@/components/app/PageHeader';
import { StatCard } from '@/components/app/StatCard';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { CurrentEditionCard } from '@/features/festival-editions/components/CurrentEditionCard';
import { useSession } from '@/features/identity-access/session';
import { useFormatters } from '@/lib/format';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { SUMMARY_STALE_TIME_MS } from '../queries';

/** The dashboard's short table of next expiries (spec: Alerts dashboard). */
const NEXT_EXPIRIES = 10;
const SHORT_DATE: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' };

/** A titled section of the dashboard, named by its heading. */
function DashboardSection({ title, children }: { title: string; children: ReactNode }) {
  const headingId = useId();
  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-group">
      <h2 id={headingId} className="text-section text-foreground">
        {title}
      </h2>
      {children}
    </section>
  );
}

/** Active, reserve and with-warnings figures, then a figure per warning, each opening the filtered list. */
function Figures({ summary }: { summary: ComplianceSummaryResponse }) {
  const { t } = useTranslation(['insights', 'ui']);
  const { number } = useFormatters();
  return (
    <>
      {/* The page's main message comes first. A lasting state, not news: no live region. */}
      {summary.withWarnings > 0 ? (
        <AlertBanner severity="warning" live={false}>
          {t('dashboard.attention', { count: summary.withWarnings })}
        </AlertBanner>
      ) : (
        <AlertBanner severity="success" live={false}>
          {t('dashboard.upToDate')}
        </AlertBanner>
      )}
      <DashboardSection title={t('dashboard.figuresTitle')}>
        {/* Two per row on phones, the third across both; three in a row from 640 px (audit). */}
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          <li className="flex">
            <StatCard
              className="w-full"
              label={t('dashboard.active')}
              value={number(summary.active)}
              to="/arquebusiers?status=ACTIVE"
            />
          </li>
          <li className="flex">
            <StatCard
              className="w-full"
              label={t('dashboard.reserve')}
              value={number(summary.reserve)}
              to="/arquebusiers?status=RESERVE"
            />
          </li>
          <li className="col-span-2 flex sm:col-span-1">
            <StatCard
              className="w-full"
              label={t('dashboard.withWarnings')}
              value={number(summary.withWarnings)}
              to="/arquebusiers?warning=ANY"
            />
          </li>
        </ul>
      </DashboardSection>
      <DashboardSection title={t('dashboard.warningsTitle')}>
        <ul className="grid grid-cols-2 gap-3 xl:grid-cols-4">
          {summary.warnings.map(({ code, count }) => (
            <li key={code} className="flex">
              <StatCard
                className="w-full"
                label={t(`ui:status.warning.${code}`)}
                value={number(count)}
                to={`/arquebusiers?warning=${code}`}
              />
            </li>
          ))}
        </ul>
      </DashboardSection>
    </>
  );
}

/** The arquebusiers whose license expires first, sorted by date and then by name. */
function NextExpiries({ rows, loading }: { rows: readonly ArquebusierRowResponse[]; loading: boolean }) {
  const { t, i18n } = useTranslation('insights');
  const { date } = useFormatters();
  const next = useMemo(() => {
    const collator = new Intl.Collator(i18n.language, { sensitivity: 'base' });
    return rows
      .filter((row) => row.licenseExpiresOn && row.warnings.includes(ComplianceWarning.LICENSE_EXPIRING))
      .sort(
        (a, b) =>
          (a.licenseExpiresOn ?? '').localeCompare(b.licenseExpiresOn ?? '') ||
          collator.compare(a.lastName, b.lastName) ||
          collator.compare(a.firstName, b.firstName),
      )
      .slice(0, NEXT_EXPIRIES);
  }, [rows, i18n.language]);
  const columns = useMemo<DataTableColumn<ArquebusierRowResponse>[]>(
    () => [
      {
        id: 'name',
        header: t('dashboard.expiries.columns.name'),
        cell: (row) => (
          <Link to={`/arquebusiers/${row.id}`} className="font-semibold text-foreground hover:underline">
            {`${row.lastName}, ${row.firstName}`}
          </Link>
        ),
      },
      { id: 'comparsa', header: t('dashboard.expiries.columns.comparsa'), cell: (row) => row.comparsaName },
      {
        id: 'expiresOn',
        header: t('dashboard.expiries.columns.expiresOn'),
        cell: (row) =>
          row.licenseExpiresOn ? date(new Date(`${row.licenseExpiresOn}T12:00:00Z`), SHORT_DATE) : '',
      },
    ],
    [t, date],
  );

  return (
    <DashboardSection title={t('dashboard.expiries.title')}>
      <DataTable
        caption={t('dashboard.expiries.caption')}
        data={next}
        columns={columns}
        getRowId={(row) => row.id}
        getRowHref={(row) => `/arquebusiers/${row.id}`}
        mobileRow={(row) => (
          <>
            <Link to={`/arquebusiers/${row.id}`} className="font-semibold text-foreground">
              {`${row.lastName}, ${row.firstName}`}
            </Link>
            <span className="text-help text-muted-foreground">
              {row.comparsaName}
              {row.licenseExpiresOn &&
                ` · ${t('dashboard.expiries.expiresOn', { date: date(new Date(`${row.licenseExpiresOn}T12:00:00Z`), SHORT_DATE) })}`}
            </span>
          </>
        )}
        paginated={false}
        isLoading={loading}
        emptyText={t('dashboard.expiries.empty')}
      />
      <Link
        to="/arquebusiers?license=EXPIRING"
        className="self-start text-primary underline-offset-4 hover:underline"
      >
        {t('dashboard.expiries.seeAll')}
      </Link>
    </DashboardSection>
  );
}

/**
 * Spec "Alerts dashboard (UC-06)": the start page shows, within the user's scope, the figures of
 * the warning summary, each linking to the arquebusier list filtered by it, a message on what needs
 * attention, and the next license expiries (dashboard template).
 */
export function DashboardPage() {
  const { t } = useTranslation();
  const { t: tRegistry } = useTranslation('registry');
  useDocumentTitle(t('home.title'));
  const session = useSession();
  const signedIn = session.status === 'signedIn';
  const isAdmin = session.account?.role === 'ADMIN';

  // Only a FiringChief's comparsas say whether they have a scope; an Admin always has one. Nothing
  // is shown before it is known, so a FiringChief without comparsas never sees figures flash.
  const comparsas = useListComparsas({ includeInactive: true }, { query: { enabled: signedIn && !isAdmin } });
  const scopeKnown = isAdmin || !comparsas.isPending;
  const unassigned =
    !isAdmin && comparsas.isSuccess && (comparsas.data.data as ComparsaResponse[]).length === 0;
  const enabled = signedIn && scopeKnown && !unassigned;
  const summary = useGetComplianceSummary({ query: { enabled, staleTime: SUMMARY_STALE_TIME_MS } });
  const arquebusiers = useListArquebusiers({}, { query: { enabled } });
  const rows = useMemo(
    () => (arquebusiers.data?.data ?? []) as ArquebusierRowResponse[],
    [arquebusiers.data],
  );

  return (
    <>
      <PageHeader title={t('home.title')} description={t('home.description')} />
      <CurrentEditionCard />
      {!scopeKnown ? null : unassigned ? (
        <EmptyState
          icon={IdCard}
          title={tRegistry('arquebusiers.unassigned.title')}
          description={tRegistry('arquebusiers.unassigned.description')}
        />
      ) : (
        <>
          {summary.isError && <LoadFailure error={summary.error} onRetry={() => summary.refetch()} />}
          {summary.isSuccess && <Figures summary={summary.data.data as ComplianceSummaryResponse} />}
          {arquebusiers.isError ? (
            <LoadFailure error={arquebusiers.error} onRetry={() => arquebusiers.refetch()} />
          ) : (
            <NextExpiries rows={rows} loading={arquebusiers.isPending} />
          )}
        </>
      )}
    </>
  );
}
