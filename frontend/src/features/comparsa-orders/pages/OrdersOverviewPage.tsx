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
import { StatFilter } from '@/components/app/StatFilter';
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

/** A row's status for the filters, "not prepared" included. */
type RowStatusKey = NonNullable<OverviewRowResponse['status']> | 'NOT_PREPARED';
const rowStatusKey = (row: OverviewRowResponse): RowStatusKey => row.status ?? 'NOT_PREPARED';

/**
 * Admins: how many orders are in each status, "not prepared" included (UC-16), each a toggle that
 * filters the table below to the chosen statuses (none chosen: every comparsa).
 */
function StatusFilters({
  overview,
  chosen,
  onChange,
}: {
  overview: OverviewResponse;
  chosen: ReadonlySet<RowStatusKey>;
  onChange: (chosen: ReadonlySet<RowStatusKey>) => void;
}) {
  const { t } = useTranslation('orders');
  const { number } = useFormatters();
  const counts = overview.statusCounts;
  if (!counts) return null;
  const figures: { id: RowStatusKey; label: string; value: number }[] = [
    { id: 'NOT_PREPARED', label: t('overview.counts.notPrepared'), value: counts.notPrepared },
    { id: 'DRAFT', label: t('overview.counts.draft'), value: counts.draft },
    { id: 'SUBMITTED', label: t('overview.counts.submitted'), value: counts.submitted },
    { id: 'RETURNED', label: t('overview.counts.returned'), value: counts.returned },
    { id: 'VALIDATED', label: t('overview.counts.validated'), value: counts.validated },
  ];
  return (
    <StatFilter
      label={t('overview.counts.title')}
      items={figures.map((figure) => ({
        id: figure.id,
        label: figure.label,
        count: number(figure.value),
        tone: figure.id === 'RETURNED' && figure.value > 0 ? 'warning' : 'neutral',
        pressed: chosen.has(figure.id),
        onPressedChange: (pressed) => {
          const next = new Set(chosen);
          if (pressed) next.add(figure.id);
          else next.delete(figure.id);
          onChange(next);
        },
      }))}
    />
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
  // The statuses chosen in the counters; none: every comparsa.
  const [chosen, setChosen] = useState<ReadonlySet<RowStatusKey>>(() => new Set());
  // Whether the counters have been used: until then there is nothing to announce.
  const [filtered, setFiltered] = useState(false);
  const choose = useCallback((next: ReadonlySet<RowStatusKey>) => {
    setChosen(next);
    setFiltered(true);
  }, []);
  const allRows = data?.rows;
  const rows = useMemo(
    () => (allRows ?? []).filter((row) => chosen.size === 0 || chosen.has(rowStatusKey(row))),
    [allRows, chosen],
  );
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
        pinned: true,
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
        statuses={edition && <EditionBadges edition={edition} />}
        actions={
          edition &&
          isAdmin && (
            <Button asChild variant="secondary" size="sm">
              <Link to={`/editions/${edition.id}/exports`}>{tExports('page.link')}</Link>
            </Button>
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
      {/* The orders come first: what the Admin reviews; the edition's totals and billing follow (audit). */}
      {(data ? edition : !overview.isError) && (
        <Section title={t('overview.caption')}>
          {data && edition && isAdmin && <StatusFilters overview={data} chosen={chosen} onChange={choose} />}
          {/* Said on every change of the counters, clearing the last one included. */}
          <p role="status" className="sr-only">
            {filtered ? t('overview.resultCount', { count: rows.length }) : ''}
          </p>
          <DataTable
            caption={t('overview.caption')}
            data={rows}
            columns={columns}
            paginated={false}
            isLoading={overview.isPending}
            emptyText={chosen.size > 0 ? t('overview.filteredEmpty') : t('overview.empty')}
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
      {data && edition && isAdmin && data.editionTotals && <EditionTotals totals={data.editionTotals} />}
      {data && edition && isAdmin && data.editionBilling && (
        <BillingSummarySection billing={data.editionBilling} scope="edition" />
      )}
    </>
  );
}
