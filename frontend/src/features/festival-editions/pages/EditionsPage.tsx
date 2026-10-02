import { CalendarDays, Plus } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useListEditions } from '@/api/generated/editions/editions';
import type { EditionRowResponse } from '@/api/generated/model';
import { NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { EmptyState } from '@/components/app/EmptyState';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useSession } from '@/features/identity-access/session';
import { useNotice } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { useEditionDates } from '../dates';

/** The status and, for the edition in progress, whether its orders are open. */
export function EditionBadges({ edition }: { edition: Pick<EditionRowResponse, 'status' | 'ordersOpen'> }) {
  return (
    <span className="flex flex-wrap items-center gap-2">
      <StatusBadge kind="edition" value={edition.status} />
      {edition.status === 'IN_PROGRESS' && (
        <StatusBadge kind="orders" value={edition.ordersOpen ? 'OPEN' : 'CLOSED'} />
      )}
    </span>
  );
}

/**
 * Spec "Editions screens" and "Edition visibility (BR-12)": every edition the caller can see, newest
 * first, the current one marked. Admins create editions here; FiringChiefs never see drafts (the
 * server decides).
 */
export function EditionsPage() {
  const { t } = useTranslation('editions');
  useDocumentTitle(t('title'));
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const editions = useListEditions({ query: { enabled: session.status === 'signedIn' } });
  const rows = useMemo(() => (editions.data?.data ?? []) as EditionRowResponse[], [editions.data]);
  const dates = useEditionDates();
  const [notice] = useNotice();

  const columns = useMemo<DataTableColumn<EditionRowResponse>[]>(
    () => [
      {
        id: 'year',
        header: t('list.columns.year'),
        rowHeader: true,
        sortValue: (edition) => edition.year,
        cell: (edition) => (
          <Link
            to={`/editions/${edition.id}`}
            className="font-semibold text-foreground underline underline-offset-4"
          >
            {t('detail.name', { year: edition.year })}
          </Link>
        ),
        secondary: (edition) => (edition.isCurrent ? t('list.current') : undefined),
      },
      {
        id: 'festival',
        header: t('list.columns.festival'),
        cell: (edition) => dates.festival(edition.festivalStartsOn, edition.festivalEndsOn),
      },
      {
        id: 'status',
        header: t('list.columns.status'),
        cell: (edition) => <EditionBadges edition={edition} />,
      },
    ],
    [t, dates],
  );

  const empty = editions.isSuccess && rows.length === 0;

  return (
    <>
      <PageHeader
        title={t('title')}
        description={isAdmin ? t('list.description') : t('list.descriptionFiringChief')}
        actions={
          isAdmin && (
            <Button asChild>
              <Link to="/editions/new">
                <Plus aria-hidden="true" />
                {t('list.new')}
              </Link>
            </Button>
          )
        }
      />
      <NoticeBanner notice={notice} />
      {editions.isError && <LoadFailure error={editions.error} onRetry={() => editions.refetch()} />}
      {empty && (
        <EmptyState
          icon={CalendarDays}
          title={isAdmin ? t('list.emptyAdmin.title') : t('list.emptyFiringChief.title')}
          description={isAdmin ? t('list.emptyAdmin.description') : t('list.emptyFiringChief.description')}
          action={
            isAdmin && (
              <Button asChild>
                <Link to="/editions/new">
                  <Plus aria-hidden="true" />
                  {t('list.new')}
                </Link>
              </Button>
            )
          }
        />
      )}
      {!editions.isError && !empty && (
        <DataTable
          caption={t('list.caption')}
          data={rows}
          columns={columns}
          paginated={false}
          isLoading={editions.isPending}
          getRowId={(edition) => edition.id}
          getRowHref={(edition) => `/editions/${edition.id}`}
          mobileRow={(edition) => (
            <>
              <Link to={`/editions/${edition.id}`} className="font-semibold text-foreground">
                {t('detail.name', { year: edition.year })}
              </Link>
              <span className="text-help text-muted-foreground">
                {dates.festival(edition.festivalStartsOn, edition.festivalEndsOn)}
              </span>
              <span className="flex flex-wrap items-center gap-2 text-help text-muted-foreground">
                {edition.isCurrent && <span>{t('list.current')}</span>}
                <EditionBadges edition={edition} />
              </span>
            </>
          )}
        />
      )}
    </>
  );
}
