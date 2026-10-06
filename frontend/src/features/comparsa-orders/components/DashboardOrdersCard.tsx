import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useGetComparsaOrdersOverview } from '@/api/generated/comparsa-orders/comparsa-orders';
import { useGetCurrentEdition } from '@/api/generated/editions/editions';
import type { CurrentEditionResponse, OverviewResponse, OverviewRowResponse } from '@/api/generated/model';
import { SectionCard } from '@/components/app/SectionCard';
import { StatusBadge } from '@/components/app/StatusBadge';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useNextWindowText } from '@/features/festival-editions/editionText';
import { useSession } from '@/features/identity-access/session';
import { useFormatters } from '@/lib/format';

const LINK = 'w-fit font-semibold text-foreground underline underline-offset-4 hover:no-underline';

/** One of a FiringChief's orders: its status, the entries with warnings and the way to it. */
function OwnOrder({ row, single }: { row: OverviewRowResponse; single: boolean }) {
  const { t } = useTranslation('orders');
  const warnings = row.totals?.entriesWithWarnings ?? 0;
  return (
    <li className="flex flex-col gap-1.5">
      <div className="flex flex-wrap items-center gap-2">
        {!single && <span className="font-semibold text-foreground">{row.comparsa.name}</span>}
        {row.status ? (
          <StatusBadge kind="order" value={row.status} />
        ) : (
          <span className="text-muted-foreground">{t('dashboardCard.notPrepared')}</span>
        )}
      </div>
      {warnings > 0 && (
        <p className="text-body text-foreground">{t('dashboardCard.withWarnings', { count: warnings })}</p>
      )}
      {row.orderId ? (
        <Link
          to={`/orders/${row.orderId}`}
          className={LINK}
          aria-label={single ? undefined : t('dashboardCard.openFor', { comparsa: row.comparsa.name })}
        >
          {t('dashboardCard.open')}
        </Link>
      ) : (
        row.canPrepare && (
          <Link to="/orders" className={LINK}>
            {t('dashboardCard.prepare')}
          </Link>
        )
      )}
    </li>
  );
}

/** An Admin's view of the edition's orders: how many wait for review and how many are in each status. */
function AllOrders({ overview }: { overview: OverviewResponse }) {
  const { t } = useTranslation('orders');
  const { number } = useFormatters();
  const counts = overview.statusCounts;
  if (!counts) return null;
  const figures = [
    { id: 'notPrepared', label: t('overview.counts.notPrepared'), value: counts.notPrepared },
    { id: 'draft', label: t('overview.counts.draft'), value: counts.draft },
    { id: 'submitted', label: t('overview.counts.submitted'), value: counts.submitted },
    { id: 'returned', label: t('overview.counts.returned'), value: counts.returned },
    { id: 'validated', label: t('overview.counts.validated'), value: counts.validated },
  ];
  return (
    <>
      <p className="font-display text-section text-foreground">
        {counts.submitted > 0
          ? t('dashboardCard.toReview', { count: counts.submitted })
          : t('dashboardCard.noneToReview')}
      </p>
      <dl aria-label={t('dashboardCard.countsLabel')} className="flex flex-wrap gap-x-4 gap-y-1 text-help">
        {figures.map((figure) => (
          <div key={figure.id} className="flex gap-1">
            <dt className="text-muted-foreground">{figure.label}</dt>
            <dd className="font-semibold text-foreground tabular-nums">{number(figure.value)}</dd>
          </div>
        ))}
      </dl>
      <Link to="/orders" className={LINK}>
        {t('dashboardCard.review')}
      </Link>
    </>
  );
}

/**
 * The orders of the current edition on the start page (UI audit): a FiringChief's own order, its
 * status, close date and entries with warnings; for Admins, how many orders wait for review. Shows
 * nothing without an edition in progress (the edition card says so); its own failure never hides
 * the rest of the page.
 */
export function DashboardOrdersCard() {
  const { t } = useTranslation('orders');
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const enabled = session.status === 'signedIn';
  const query = useGetComparsaOrdersOverview(undefined, { query: { enabled } });
  const current = useGetCurrentEdition({ query: { enabled } });
  const nextWindow = useNextWindowText();
  const overview = query.data?.data as OverviewResponse | undefined;
  const edition = overview?.edition;
  const currentEdition = (current.data?.data as CurrentEditionResponse | undefined)?.edition;

  if (query.isError) {
    return (
      <LoadFailure
        error={query.error}
        consequence={t('dashboardCard.loadFailed')}
        onRetry={() => query.refetch()}
      />
    );
  }
  if (!overview || !edition || (!isAdmin && overview.rows.length === 0)) return null;

  const title = isAdmin
    ? t('dashboardCard.allTitle', { year: edition.year })
    : t('dashboardCard.mineTitle', { year: edition.year, count: overview.rows.length });
  return (
    <SectionCard title={title}>
      <div className="flex flex-col gap-3">
        {currentEdition?.id === edition.id && (
          <p className="text-body text-muted-foreground">{nextWindow(currentEdition)}</p>
        )}
        {isAdmin ? (
          <AllOrders overview={overview} />
        ) : (
          // eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`.
          <ul role="list" className="flex flex-col gap-4">
            {overview.rows.map((row) => (
              <OwnOrder key={row.comparsa.id} row={row} single={overview.rows.length === 1} />
            ))}
          </ul>
        )}
      </div>
    </SectionCard>
  );
}
