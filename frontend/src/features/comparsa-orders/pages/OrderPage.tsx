import { useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { Fragment, useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { useGetComparsa } from '@/api/generated/comparsas/comparsas';
import { useAddEditionEntry, useGetComparsaOrder } from '@/api/generated/comparsa-orders/comparsa-orders';
import type { ComparsaResponse, EntryResponse, OrderResponse } from '@/api/generated/model';
import { ApiProblemError } from '@/api/http';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ComparsaLogo } from '@/components/app/ComparsaLogo';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { KeyFacts } from '@/components/app/KeyFacts';
import { PageHeader } from '@/components/app/PageHeader';
import { RecordHeader } from '@/components/app/RecordHeader';
import { SectionCard } from '@/components/app/SectionCard';
import { StatusBadge } from '@/components/app/StatusBadge';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useSession } from '@/features/identity-access/session';
import { logoUrl } from '@/features/federation-catalog/logos';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useFormatters } from '@/lib/format';
import { useNotice } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { personName, useEntryText } from '../orderText';
import { EntryEditSheet } from '../components/EntryEditSheet';
import { OrderActions } from '../components/OrderActions';
import { needsReload, problemCode, problemMessage } from '../problems';
import { orderChanged, reloadOrder } from '../queries';

const ENTRIES_ID = 'entries';

/** The order's totals at a glance (UC-16). */
function OrderTotals({ order }: { order: OrderResponse }) {
  const { t } = useTranslation('orders');
  const { number } = useFormatters();
  const { totals } = order;
  const rentals = totals.weaponRentals.reduce((sum, rental) => sum + rental.count, 0);
  return (
    <KeyFacts
      label={t('order.totals')}
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
        { id: 'rentals', label: t('totals.rentals'), value: number(rentals) },
      ]}
    />
  );
}

/** Why the order is read-only, who submitted or returned it, and what needs attention. */
function OrderMessages({ order }: { order: OrderResponse }) {
  const { t } = useTranslation('orders');
  const { date } = useFormatters();
  const text = useEntryText();
  const blocked = order.entries.filter((entry) => entry.issues.length > 0);
  return (
    <>
      {!order.canEdit && order.readOnlyReason && (
        <AlertBanner severity="info" live={false}>
          {order.readOnlyReason === 'validated'
            ? t('order.readOnly.validated')
            : t('order.readOnly.ordersClosed')}
        </AlertBanner>
      )}
      {order.status === 'RETURNED' && order.review?.returnReason && (
        <AlertBanner
          severity="warning"
          live={false}
          title={t('order.returned', { name: order.review.byName ?? t('order.someone') })}
        >
          <span className="whitespace-pre-line">{order.review.returnReason}</span>
        </AlertBanner>
      )}
      {order.submission?.byAdmin && (
        <AlertBanner severity="info" live={false}>
          {t('order.submittedByAdmin', {
            name: order.submission.byName ?? t('order.someone'),
            date: date(new Date(order.submission.at), { dateStyle: 'long' }),
          })}
        </AlertBanner>
      )}
      {order.totals.entriesWithWarnings > 0 && (
        <AlertBanner severity="warning" live={false}>
          <a href={`#${ENTRIES_ID}`} className="font-semibold underline underline-offset-4">
            {t('order.warnings', { count: order.totals.entriesWithWarnings })}
          </a>
        </AlertBanner>
      )}
      {blocked.length > 0 && (
        <AlertBanner severity="error" live={false} title={t('order.issuesTitle')}>
          <ul className="list-disc pl-5">
            {blocked.map((entry) => (
              <li key={entry.id}>
                {t('order.issue', {
                  name: personName(entry.arquebusier),
                  issues: entry.issues.map(text.issue).join(', '),
                })}
              </li>
            ))}
          </ul>
          <a href={`#${ENTRIES_ID}`} className="font-semibold underline underline-offset-4">
            {t('order.toEntries')}
          </a>
        </AlertBanner>
      )}
    </>
  );
}

/** An entry's notes on one line: the dots are seen, a comma pause is heard instead. */
function EntryNotes({ notes }: { notes: string[] }) {
  return (
    <>
      {notes.map((note, index) => (
        <Fragment key={note}>
          {index > 0 && (
            <>
              <span aria-hidden="true"> · </span>
              <span className="sr-only">, </span>
            </>
          )}
          {note}
        </Fragment>
      ))}
    </>
  );
}

/** The entry's compliance warnings in words (BR-04); none for reserves and past editions. */
function EntryWarnings({ entry }: { entry: EntryResponse }) {
  if (entry.warnings.length === 0) return null;
  return (
    <span className="flex flex-wrap gap-1">
      {entry.warnings.map((warning) => (
        <StatusBadge key={warning} kind="warning" value={warning} />
      ))}
    </span>
  );
}

/**
 * The "Edit" action of one entry, reading the order from the cache, so the table's columns stay
 * stable and an open panel survives the order being refetched.
 */
function EntryEditAction({
  orderId,
  entryId,
  onClosedOut,
}: {
  orderId: string;
  entryId: string;
  onClosedOut: (reason: string) => void;
}) {
  const query = useGetComparsaOrder(orderId, { query: { retry: false } });
  const order = query.data?.data as OrderResponse | undefined;
  const entry = order?.entries.find((candidate) => candidate.id === entryId);
  return order?.canEdit && entry ? (
    <EntryEditSheet order={order} entry={entry} onClosedOut={onClosedOut} />
  ) : null;
}

/**
 * One comparsa's order (spec: Orders screens): the detail template with its totals, the messages
 * that need attention, the entries, the arquebusiers not in it and the weapons lent to others.
 * Entries are edited and never removed.
 */
export function OrderPage() {
  const { t } = useTranslation('orders');
  const { orderId = '' } = useParams();
  const queryClient = useQueryClient();
  const query = useGetComparsaOrder(orderId, { query: { retry: false } });
  const order = query.data?.data as OrderResponse | undefined;
  const comparsaQuery = useGetComparsa(order?.comparsa.id ?? '', {
    query: { enabled: order !== undefined, retry: false },
  });
  const comparsa = comparsaQuery.data?.data as ComparsaResponse | undefined;
  const title = order ? t('order.title', { comparsa: order.comparsa.name }) : t('overview.title');
  useDocumentTitle(title);
  const text = useEntryText();
  const [notice, announce] = useNotice();
  const add = useAddEditionEntry();
  const isAdmin = useSession().account?.role === 'ADMIN';
  const [adding, setAdding] = useState<string>();
  const canEdit = order?.canEdit === true;
  // Called once the panel is gone (the reload removed it): announced on the page, which takes focus.
  const onClosedOut = useCallback(
    (reason: string) => {
      window.setTimeout(() => {
        announce('error', reason);
      }, 0);
    },
    [announce],
  );

  const columns = useMemo<DataTableColumn<EntryResponse>[]>(
    () => [
      {
        id: 'name',
        header: t('entries.columns.name'),
        rowHeader: true,
        cell: (entry) => <span className="font-semibold">{personName(entry.arquebusier)}</span>,
        secondary: (entry) => {
          const notes = text.notes(entry);
          return notes.length > 0 ? <EntryNotes notes={notes} /> : undefined;
        },
      },
      {
        id: 'status',
        header: t('entries.columns.status'),
        cell: (entry) => <StatusBadge kind="arquebusier" value={entry.status} />,
      },
      { id: 'powder', header: t('entries.columns.powder'), cell: text.powder, align: 'end' },
      { id: 'caps', header: t('entries.columns.caps'), cell: text.caps },
      { id: 'weapon', header: t('entries.columns.weapon'), cell: text.weapon },
      { id: 'flask', header: t('entries.columns.flask'), cell: text.flask },
      {
        id: 'warnings',
        header: t('entries.columns.warnings'),
        cell: (entry) => <EntryWarnings entry={entry} />,
      },
      ...(canEdit
        ? [
            {
              id: 'action',
              header: t('entries.columns.action'),
              hideHeader: true,
              cell: (entry: EntryResponse) => (
                <EntryEditAction orderId={orderId} entryId={entry.id} onClosedOut={onClosedOut} />
              ),
            },
          ]
        : []),
    ],
    // Stable while the order is edited: a new column set would remount the open panel.
    [t, text, canEdit, orderId, onClosedOut],
  );

  if (query.isError && query.error instanceof ApiProblemError && query.error.status === 404) {
    return <NotFoundPage />;
  }
  if (!order) {
    return (
      <>
        <PageHeader title={t('overview.title')} />
        {query.isError ? (
          <LoadFailure error={query.error} onRetry={() => query.refetch()} />
        ) : (
          <p role="status">{t('order.loading')}</p>
        )}
      </>
    );
  }

  const addArquebusier = (arquebusierId: string, name: string) => {
    if (adding) return;
    setAdding(arquebusierId);
    add.mutate(
      { id: order.id, data: { arquebusierId } },
      {
        onSuccess: (response) => {
          void orderChanged(queryClient, response.data as OrderResponse);
          announce('success', t('notInOrder.added', { name }));
        },
        onError: (error) => {
          // Someone else added them meanwhile, or the order changed: show it as it is now.
          if (needsReload(error) || problemCode(error) === 'orders.alreadyInEdition') {
            void reloadOrder(queryClient, order.id);
          }
          announce('error', problemMessage(t, error));
        },
        onSettled: () => {
          setAdding(undefined);
        },
      },
    );
  };

  return (
    <>
      <RecordHeader
        media={<ComparsaLogo src={comparsa ? logoUrl(comparsa.id, comparsa.logo) : null} size="lg" />}
        context={t('order.edition', { year: order.edition.year })}
        name={title}
        statuses={
          <>
            <StatusBadge kind="order" value={order.status} />
            {order.edition.status === 'IN_PROGRESS' && (
              <StatusBadge kind="orders" value={order.edition.ordersOpen ? 'OPEN' : 'CLOSED'} />
            )}
          </>
        }
        actions={<OrderActions order={order} isAdmin={isAdmin} announce={announce} />}
        back={{ to: `/editions/${order.edition.id}/orders`, label: t('order.back') }}
      />
      <NoticeBanner notice={notice} />
      {query.isRefetchError && (
        <LoadFailure
          error={query.error}
          consequence={t('order.staleFailed')}
          onRetry={() => query.refetch()}
        />
      )}
      <OrderMessages order={order} />
      <OrderTotals order={order} />
      <section
        id={ENTRIES_ID}
        aria-labelledby={`${ENTRIES_ID}-title`}
        className="flex scroll-mt-24 flex-col gap-group"
      >
        <h2 id={`${ENTRIES_ID}-title`} className="text-section text-foreground">
          {t('entries.caption')}
        </h2>
        <DataTable
          caption={t('entries.caption')}
          data={order.entries}
          columns={columns}
          paginated={false}
          getRowId={(entry) => entry.id}
          mobileRow={(entry) => (
            <>
              <span className="font-semibold">{personName(entry.arquebusier)}</span>
              {text.notes(entry).length > 0 && (
                <span className="text-help text-muted-foreground">
                  <EntryNotes notes={text.notes(entry)} />
                </span>
              )}
              <StatusBadge kind="arquebusier" value={entry.status} />
              <dl className="flex flex-col gap-1 text-help">
                {[
                  ['powder', t('entries.columns.powder'), text.powder(entry)],
                  ['caps', t('entries.columns.caps'), text.caps(entry)],
                  ['weapon', t('entries.columns.weapon'), text.weapon(entry)],
                  ['flask', t('entries.columns.flask'), text.flask(entry)],
                ].map(([key, label, value]) => (
                  <div key={key}>
                    <dt className="inline text-muted-foreground">{label}: </dt>
                    <dd className="inline">{value}</dd>
                  </div>
                ))}
              </dl>
              <EntryWarnings entry={entry} />
              {canEdit && <EntryEditAction orderId={orderId} entryId={entry.id} onClosedOut={onClosedOut} />}
            </>
          )}
        />
      </section>
      {order.notInOrder.length > 0 && (
        <SectionCard title={t('notInOrder.title')} description={t('notInOrder.description')} span="full">
          {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
          <ul role="list" className="flex flex-col gap-2">
            {order.notInOrder.map((person) => {
              const name = personName(person);
              return (
                <li key={person.arquebusierId} className="flex flex-wrap items-center justify-between gap-2">
                  <span>{name}</span>
                  {canEdit && (
                    <Button
                      type="button"
                      size="sm"
                      variant="secondary"
                      icon={Plus}
                      pending={adding === person.arquebusierId}
                      aria-label={t('notInOrder.addFor', { name })}
                      onClick={() => {
                        addArquebusier(person.arquebusierId, name);
                      }}
                    >
                      {t('notInOrder.add')}
                    </Button>
                  )}
                </li>
              );
            })}
          </ul>
        </SectionCard>
      )}
      {order.lentOut.length > 0 && (
        <SectionCard title={t('lentOut.title')} description={t('lentOut.description')} span="full">
          {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
          <ul role="list" className="flex flex-col gap-2">
            {order.lentOut.map((loan) => (
              <li key={loan.loanId}>
                {t('lentOut.item', {
                  weapon: [loan.weaponModel?.label, loan.weaponNumber].filter(Boolean).join(' '),
                  owner: personName({ firstName: loan.lenderFirstName, lastName: loan.lenderLastName }),
                  borrower: personName({
                    firstName: loan.borrowerFirstName,
                    lastName: loan.borrowerLastName,
                  }),
                  comparsa: loan.borrowerComparsaName,
                })}
              </li>
            ))}
          </ul>
        </SectionCard>
      )}
    </>
  );
}
