import { useQueryClient } from '@tanstack/react-query';
import { ClipboardList, Plus } from 'lucide-react';
import { useCallback, useId, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, useNavigate, useParams } from 'react-router';
import {
  getGetComparsaOrdersOverviewQueryKey,
  useGetComparsaOrdersOverview,
  usePrepareComparsaOrder,
} from '@/api/generated/comparsa-orders/comparsa-orders';
import type {
  OrderEditionResponse,
  OrderResponse,
  OrderTotalsResponse,
  OverviewResponse,
  OverviewRowResponse,
} from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Breakdown } from '@/components/app/Breakdown';
import { Button } from '@/components/app/Button';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { EmptyState } from '@/components/app/EmptyState';
import { KeyFacts } from '@/components/app/KeyFacts';
import { PageHeader } from '@/components/app/PageHeader';
import { StatCard } from '@/components/app/StatCard';
import { StatusBadge } from '@/components/app/StatusBadge';
import { BillingAmount, BillingTotal } from '@/features/billing/components/BillingAmount';
import { BillingSummarySection } from '@/features/billing/components/BillingSummarySection';
import { ApiProblemError } from '@/api/http';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useSession } from '@/features/identity-access/session';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useFormatters } from '@/lib/format';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemMessage } from '../problems';
import { orderChanged } from '../queries';

/** A titled section of the dashboard, named by its heading. */
function Section({ title, children }: { title: string; children: ReactNode }) {
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

/** Whether the orders are open, for the edition in progress (BR-10). */
function EditionBadges({ edition }: { edition: OrderEditionResponse }) {
  return (
    <span className="flex flex-wrap items-center gap-2">
      <StatusBadge kind="edition" value={edition.status} />
      {edition.status === 'IN_PROGRESS' && (
        <StatusBadge kind="orders" value={edition.ordersOpen ? 'OPEN' : 'CLOSED'} />
      )}
    </span>
  );
}

/** An order's status, or "not prepared". */
function RowStatus({ row }: { row: OverviewRowResponse }) {
  const { t } = useTranslation('orders');
  return row.status ? (
    <StatusBadge kind="order" value={row.status} />
  ) : (
    <span className="text-muted-foreground">{t('overview.notPrepared')}</span>
  );
}

/** The few totals a row shows: entries, powder and caps. */
function useTotalsText() {
  const { t } = useTranslation('orders');
  const { number } = useFormatters();
  return useCallback(
    (totals: OrderTotalsResponse | null) =>
      totals
        ? t('overview.rowTotals', {
            active: number(totals.active),
            reserve: number(totals.reserve),
            powder: number(totals.powderKg),
          })
        : t('overview.noTotals'),
    [t, number],
  );
}

/** "Prepare order" for one comparsa, named after it for assistive technology. */
function PrepareButton({
  comparsa,
  pending,
  onPrepare,
}: {
  comparsa: string;
  pending: boolean;
  onPrepare: () => void;
}) {
  const { t } = useTranslation('orders');
  return (
    <Button
      type="button"
      size="sm"
      variant="secondary"
      icon={Plus}
      pending={pending}
      aria-label={t('overview.prepareFor', { comparsa })}
      onClick={onPrepare}
    >
      {t('overview.prepare')}
    </Button>
  );
}

/** Admins: how many orders are in each status, "not prepared" included (UC-16). */
function StatusFigures({ overview }: { overview: OverviewResponse }) {
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
    <Section title={t('overview.counts.title')}>
      {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
      <ul role="list" className="grid gap-3 sm:grid-cols-3 xl:grid-cols-5">
        {figures.map((figure) => (
          <li key={figure.id} className="flex">
            <StatCard className="w-full" label={figure.label} value={number(figure.value)} />
          </li>
        ))}
      </ul>
    </Section>
  );
}

/** Admins: the totals of every order of the edition, and the rentals by model (UC-16). */
function EditionTotals({ totals }: { totals: OrderTotalsResponse }) {
  const { t } = useTranslation('orders');
  const { number } = useFormatters();
  const rentals = totals.weaponRentals.reduce((sum, rental) => sum + rental.count, 0);
  return (
    <Section title={t('overview.totals.title')}>
      <KeyFacts
        label={t('overview.totals.title')}
        items={[
          { id: 'active', label: t('totals.active'), value: number(totals.active) },
          { id: 'reserve', label: t('totals.reserve'), value: number(totals.reserve) },
          {
            id: 'powder',
            label: t('totals.powder'),
            value: t('totals.kg', { value: number(totals.powderKg) }),
          },
          {
            id: 'caps',
            label: t('totals.caps'),
            value: t('totals.capsValue', {
              normal: number(totals.normalCapsBoxes),
              small: number(totals.smallCapsBoxes),
            }),
          },
          {
            id: 'flasks',
            label: t('totals.flasks'),
            value: t('totals.flasksValue', {
              one: number(totals.flaskRentals1Kg),
              two: number(totals.flaskRentals2Kg),
            }),
          },
          { id: 'loans', label: t('totals.loans'), value: number(totals.loans) },
          { id: 'owned', label: t('totals.ownedWeapons'), value: number(totals.ownedWeapons) },
          { id: 'warnings', label: t('totals.withWarnings'), value: number(totals.entriesWithWarnings) },
        ]}
      />
      {totals.weaponRentals.length > 0 && (
        <Breakdown
          title={t('totals.rentalsTitle')}
          categoryLabel={t('totals.model')}
          columns={[{ id: 'count', label: t('totals.rentals') }]}
          rows={totals.weaponRentals.map((rental) => ({
            id: rental.weaponModel.id,
            label: rental.weaponModel.label,
            counts: [rental.count],
          }))}
          total={rentals}
        />
      )}
    </Section>
  );
}

/**
 * The orders of an edition, the current one by default (spec: Orders screens; UC-16): the dashboard
 * for Admins, the list of their comparsas for FiringChiefs. A FiringChief with a single comparsa
 * whose order is prepared is taken straight to it.
 */
export function OrdersOverviewPage() {
  const { t } = useTranslation('orders');
  const { t: tBilling } = useTranslation('billing');
  const { t: tUi } = useTranslation('ui');
  const { t: tExports } = useTranslation('exports');
  const { editionId } = useParams();
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const totalsText = useTotalsText();
  const overview = useGetComparsaOrdersOverview(editionId ? { editionId } : undefined, {
    query: { enabled: session.status === 'signedIn' },
  });
  const data = overview.data?.data as OverviewResponse | undefined;
  const edition = data?.edition ?? null;
  useDocumentTitle(edition ? t('overview.titleYear', { year: edition.year }) : t('overview.title'));
  const prepare = usePrepareComparsaOrder();
  // The comparsa being prepared; other "Prepare order" clicks wait for it.
  const [preparing, setPreparing] = useState<string>();
  // A refusal belongs to the edition it happened in: browsing another one hides it.
  const [failure, setFailure] = useState<{ editionId: string; text: string }>();
  const { mutate } = prepare;
  const startPrepare = useCallback(
    (row: OverviewRowResponse) => {
      if (!edition || preparing) return;
      setFailure(undefined);
      setPreparing(row.comparsa.id);
      mutate(
        { data: { editionId: edition.id, comparsaId: row.comparsa.id } },
        {
          onSuccess: (response) => {
            // Refusals throw (ApiProblemError), so a response here is the prepared order. It is cached
            // before navigating, so the order page opens on it.
            const order = response.data as OrderResponse;
            void orderChanged(queryClient, order);
            void navigate(`/orders/${order.id}`);
          },
          onError: (error) => {
            setFailure({ editionId: edition.id, text: problemMessage(t, error) });
            // Prepared by someone else, or the orders closed: show the rows as they are now.
            void queryClient.invalidateQueries({ queryKey: getGetComparsaOrdersOverviewQueryKey() });
          },
          onSettled: () => {
            setPreparing(undefined);
          },
        },
      );
    },
    [edition, preparing, mutate, navigate, queryClient, t],
  );

  const columns = useMemo<DataTableColumn<OverviewRowResponse>[]>(
    () => [
      {
        id: 'comparsa',
        header: t('overview.columns.comparsa'),
        rowHeader: true,
        sortValue: (row) => row.comparsa.name,
        cell: (row) =>
          row.orderId ? (
            <Link
              to={`/orders/${row.orderId}`}
              className="font-semibold text-foreground underline underline-offset-4"
            >
              {row.comparsa.name}
            </Link>
          ) : (
            <span className="font-semibold">{row.comparsa.name}</span>
          ),
      },
      {
        id: 'status',
        header: t('overview.columns.status'),
        sortValue: (row) => (row.status ? tUi(`status.order.${row.status}`) : t('overview.notPrepared')),
        cell: (row) => <RowStatus row={row} />,
      },
      {
        id: 'totals',
        header: t('overview.columns.totals'),
        // By the people in the order, the first figure of the cell.
        sortValue: (row) => (row.totals ? row.totals.active + row.totals.reserve : -1),
        cell: (row) => totalsText(row.totals),
      },
      {
        id: 'amount',
        header: tBilling('overview.amount'),
        align: 'end',
        sortValue: (row) => row.billing?.total ?? -1,
        cell: (row) => (row.billing ? <BillingTotal billing={row.billing} /> : null),
      },
      {
        id: 'billingState',
        header: tBilling('overview.billingState'),
        sortValue: (row) => (row.billing ? tUi(`status.billing.${row.billing.state}`) : ''),
        cell: (row) => (row.billing ? <StatusBadge kind="billing" value={row.billing.state} /> : null),
      },
      {
        id: 'action',
        header: t('overview.columns.action'),
        hideHeader: true,
        cell: (row) =>
          row.canPrepare && edition ? (
            <PrepareButton
              comparsa={row.comparsa.name}
              pending={preparing === row.comparsa.id}
              onPrepare={() => {
                startPrepare(row);
              }}
            />
          ) : null,
      },
    ],
    [t, tUi, tBilling, totalsText, edition, preparing, startPrepare],
  );

  if (overview.isError && overview.error instanceof ApiProblemError && overview.error.status === 404) {
    return <NotFoundPage />;
  }

  // Only from "Orders" (the current edition): an edition's own overview stays reachable from its order.
  const single = data?.rows.length === 1 ? data.rows[0] : undefined;
  if (!isAdmin && !editionId && single?.orderId) {
    return <Navigate to={`/orders/${single.orderId}`} replace />;
  }

  return (
    <>
      <PageHeader
        title={edition ? t('overview.titleYear', { year: edition.year }) : t('overview.title')}
        description={isAdmin ? t('overview.descriptionAdmin') : t('overview.descriptionFiringChief')}
        actions={
          edition && (
            <>
              <EditionBadges edition={edition} />
              {isAdmin && (
                <Button asChild variant="secondary" size="sm">
                  <Link to={`/editions/${edition.id}/exports`}>{tExports('page.link')}</Link>
                </Button>
              )}
            </>
          )
        }
      />
      {failure && failure.editionId === edition?.id && (
        <AlertBanner severity="error" focusOnMount>
          {failure.text}
        </AlertBanner>
      )}
      {overview.isError && <LoadFailure error={overview.error} onRetry={() => overview.refetch()} />}
      {data && !edition && (
        <EmptyState
          icon={ClipboardList}
          title={t('overview.noEdition.title')}
          description={t('overview.noEdition.description')}
          action={
            <Button asChild variant="secondary">
              <Link to="/editions">{t('overview.noEdition.link')}</Link>
            </Button>
          }
        />
      )}
      {data && edition && isAdmin && <StatusFigures overview={data} />}
      {data && edition && isAdmin && data.editionTotals && <EditionTotals totals={data.editionTotals} />}
      {data && edition && isAdmin && data.editionBilling && (
        <BillingSummarySection billing={data.editionBilling} scope="edition" />
      )}
      {(data ? edition : !overview.isError) && (
        <Section title={t('overview.caption')}>
          <DataTable
            caption={t('overview.caption')}
            data={data?.rows ?? []}
            columns={columns}
            paginated={false}
            isLoading={overview.isPending}
            emptyText={t('overview.empty')}
            getRowId={(row) => row.comparsa.id}
            mobileRow={(row) => (
              <>
                {row.orderId ? (
                  <Link to={`/orders/${row.orderId}`} className="font-semibold text-foreground">
                    {row.comparsa.name}
                  </Link>
                ) : (
                  <span className="font-semibold">{row.comparsa.name}</span>
                )}
                <RowStatus row={row} />
                <span className="text-help text-muted-foreground">{totalsText(row.totals)}</span>
                {row.billing && (
                  <span className="flex flex-wrap items-center gap-1 text-help">
                    <span className="text-muted-foreground">{tBilling('overview.amount')}:</span>
                    <BillingAmount billing={row.billing} />
                  </span>
                )}
                {row.canPrepare && edition && (
                  <PrepareButton
                    comparsa={row.comparsa.name}
                    pending={preparing === row.comparsa.id}
                    onPrepare={() => {
                      startPrepare(row);
                    }}
                  />
                )}
              </>
            )}
          />
        </Section>
      )}
    </>
  );
}
