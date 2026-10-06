import { Flag, Plus } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import { Side, type ComparsaResponse, type ListComparsasParams } from '@/api/generated/model';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { CheckboxField } from '@/components/app/CheckboxField';
import { ComparsaLogo } from '@/components/app/ComparsaLogo';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { EmptyState } from '@/components/app/EmptyState';
import { FilterBar } from '@/components/app/FilterBar';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { CategoryTag } from '@/components/app/Tag';
import { useSession } from '@/features/identity-access/session';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { useFormatters } from '@/lib/format';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { useNotice } from '@/lib/notices';
import { useComparsaFigures } from '../comparsaFigures';
import { logoUrl } from '../logos';
import { problemMessage } from '../problems';

const SIDES = Object.values(Side);

/**
 * Specs "Comparsas" and "Comparsa visibility (BR-12)". An Admin sees every comparsa, with
 * filters and the "New comparsa" action; a FiringChief sees all of theirs (one or two, active or
 * not), read-only. The server decides the scope; the page only shapes the view. Admins also
 * manage the Federation's logo here (spec: Federation logo).
 */
export function ComparsasPage() {
  const { t } = useTranslation('catalog');
  const { t: tUi } = useTranslation('ui');
  useDocumentTitle(t('comparsas.title'));
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const [search, setSearch] = useSearchParams();
  const side = knownFilter(search.get('side'), SIDES);
  const includeInactive = search.get('includeInactive') === 'true';
  const params: ListComparsasParams = isAdmin
    ? { ...(side ? { side } : {}), ...(includeInactive ? { includeInactive } : {}) }
    : { includeInactive: true };
  const comparsas = useListComparsas(params, { query: { enabled: session.status === 'signedIn' } });
  const rows = useMemo(() => (comparsas.data?.data ?? []) as ComparsaResponse[], [comparsas.data]);
  // Each comparsa's figures beside it, once they load (UI audit, item 17).
  const figures = useComparsaFigures();
  const { number } = useFormatters();

  const columns = useMemo<DataTableColumn<ComparsaResponse>[]>(
    () => [
      {
        id: 'name',
        header: t('comparsas.columns.name'),
        rowHeader: true,
        sortValue: (comparsa) => comparsa.name,
        cell: (comparsa) => (
          <span className="flex items-center gap-2.5">
            <ComparsaLogo src={logoUrl(comparsa.id, comparsa.logo)} size="sm" />
            <Link to={`/comparsas/${comparsa.id}`} className="font-semibold text-foreground hover:underline">
              {comparsa.name}
            </Link>
          </span>
        ),
      },
      {
        id: 'side',
        header: t('comparsas.columns.side'),
        sortValue: (comparsa) => tUi(`tag.side.${comparsa.side}`),
        cell: (comparsa) => <CategoryTag category="side" value={comparsa.side} />,
      },
      {
        id: 'status',
        header: t('comparsas.columns.status'),
        sortValue: (comparsa) => tUi(`status.catalog.${comparsa.active ? 'ACTIVE' : 'INACTIVE'}`),
        cell: (comparsa) => <StatusBadge kind="catalog" value={comparsa.active ? 'ACTIVE' : 'INACTIVE'} />,
      },
      ...(figures
        ? [
            {
              id: 'active',
              header: t('comparsas.columns.active'),
              align: 'end' as const,
              sortValue: (comparsa: ComparsaResponse) => figures.byComparsa.get(comparsa.id)?.active ?? 0,
              cell: (comparsa: ComparsaResponse) => number(figures.byComparsa.get(comparsa.id)?.active ?? 0),
            },
            {
              id: 'withWarnings',
              header: t('comparsas.columns.withWarnings'),
              align: 'end' as const,
              sortValue: (comparsa: ComparsaResponse) =>
                figures.byComparsa.get(comparsa.id)?.activeWithWarnings ?? 0,
              cell: (comparsa: ComparsaResponse) =>
                number(figures.byComparsa.get(comparsa.id)?.activeWithWarnings ?? 0),
            },
            ...(figures.year !== undefined
              ? [
                  {
                    id: 'order',
                    header: t('comparsas.columns.order'),
                    sortValue: (comparsa: ComparsaResponse) => {
                      const status = figures.byComparsa.get(comparsa.id)?.order?.status;
                      return status ? tUi(`status.order.${status}`) : '';
                    },
                    cell: (comparsa: ComparsaResponse) => {
                      const order = figures.byComparsa.get(comparsa.id)?.order;
                      if (!order) return null;
                      return order.status ? (
                        <StatusBadge kind="order" value={order.status} />
                      ) : (
                        <span className="text-muted-foreground">{t('comparsas.facts.notPrepared')}</span>
                      );
                    },
                  },
                ]
              : []),
          ]
        : []),
    ],
    [t, tUi, figures, number],
  );

  const setFilter = (key: 'side' | 'includeInactive', value: string): void => {
    setSearch(withFilter(search, key, value), { replace: true });
  };

  const unassigned = !isAdmin && comparsas.isSuccess && rows.length === 0;
  const [notice] = useNotice();

  return (
    <>
      <PageHeader
        title={t('comparsas.title')}
        description={isAdmin ? t('comparsas.description') : t('comparsas.descriptionFiringChief')}
        actions={
          isAdmin && (
            <Button asChild>
              <Link to="/comparsas/new">
                <Plus aria-hidden="true" />
                {t('comparsas.new')}
              </Link>
            </Button>
          )
        }
      />
      <NoticeBanner notice={notice} />
      {isAdmin && (
        <FilterBar
          resultText={comparsas.isSuccess ? t('comparsas.resultCount', { count: rows.length }) : ''}
          filters={
            <>
              <FilterSelect
                label={t('comparsas.filters.side')}
                value={side}
                onChange={(value) => {
                  setFilter('side', value);
                }}
                options={[
                  { value: '', label: t('comparsas.filters.all') },
                  ...SIDES.map((value) => ({ value, label: t(`side.${value}`) })),
                ]}
              />
              <div className="flex min-h-control items-center">
                <CheckboxField
                  label={t('comparsas.filters.includeInactive')}
                  checked={includeInactive}
                  onCheckedChange={(checked) => {
                    setFilter('includeInactive', checked ? 'true' : '');
                  }}
                />
              </div>
            </>
          }
        />
      )}
      {comparsas.isError && <AlertBanner severity="error">{problemMessage(t, comparsas.error)}</AlertBanner>}
      {unassigned && (
        <EmptyState
          icon={Flag}
          title={t('comparsas.unassigned.title')}
          description={t('comparsas.unassigned.description')}
        />
      )}
      {!comparsas.isError && !unassigned && (
        <DataTable
          caption={t('comparsas.caption')}
          data={rows}
          columns={columns}
          getRowId={(comparsa) => comparsa.id}
          getRowHref={(comparsa) => `/comparsas/${comparsa.id}`}
          mobileRow={(comparsa) => (
            <>
              <span className="flex items-center gap-2.5">
                <ComparsaLogo src={logoUrl(comparsa.id, comparsa.logo)} size="sm" />
                <Link to={`/comparsas/${comparsa.id}`} className="font-semibold text-foreground">
                  {comparsa.name}
                </Link>
              </span>
              <span className="flex flex-wrap items-center gap-2 text-help text-muted-foreground">
                {/* Each value with its term for screen readers (WCAG 1.3.1). */}
                <span>
                  <span className="sr-only">{t('comparsas.columns.side')}: </span>
                  <CategoryTag category="side" value={comparsa.side} />
                </span>
                <span>
                  <span className="sr-only">{t('comparsas.columns.status')}: </span>
                  <StatusBadge kind="catalog" value={comparsa.active ? 'ACTIVE' : 'INACTIVE'} />
                </span>
              </span>
              {figures && (
                <span className="text-help text-muted-foreground">
                  {t('comparsas.figuresLine', {
                    active: number(figures.byComparsa.get(comparsa.id)?.active ?? 0),
                    warnings: number(figures.byComparsa.get(comparsa.id)?.activeWithWarnings ?? 0),
                  })}
                </span>
              )}
            </>
          )}
          isLoading={comparsas.isPending}
          emptyText={t('comparsas.empty.description')}
        />
      )}
    </>
  );
}
