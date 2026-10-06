import { Trash2 } from 'lucide-react';
import { useCallback, useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import {
  getDownloadPickupAuthorisationUrl,
  useListPickupProxies,
  useRemovePickupProxy,
} from '@/api/generated/distribution/distribution';
import type { ComparsaResponse, DistributionPlanResponse, ProxyResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { FilterSelect } from '@/components/app/FilterSelect';
import { useSaveNotice } from '@/components/app/save-notice';
import { SectionCard } from '@/components/app/SectionCard';
import { StatusBadge } from '@/components/app/StatusBadge';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { DownloadButtons } from '@/features/exports/components/DownloadButtons';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { isStale, problemCode, problemMessage } from '../problems';
import { useDistributionRefresh } from '../queries';
import { ProxySheet } from './ProxySheet';

interface RowActionsProps {
  proxy: ProxyResponse;
  canRemove: boolean;
  /** Where focus goes once this row is removed: "Add proxy", or the list when it is not shown. */
  focusAfterRemoval: () => void;
}

/** "Print form" while the proxy holds, and "Remove" while the user may change proxies. */
function RowActions({ proxy, canRemove, focusAfterRemoval }: RowActionsProps) {
  const { t } = useTranslation('distribution');
  const remove = useRemovePickupProxy();
  const refresh = useDistributionRefresh(proxy.editionId);
  const notify = useSaveNotice();
  const names = { holder: proxy.holder.name, type: t(`proxies.typeName.${proxy.type}`) };
  return (
    <div className="flex flex-wrap items-start justify-end gap-2">
      {proxy.problem === null && (
        <DownloadButtons
          what={t('proxies.formWhat', names)}
          urls={{ pdf: getDownloadPickupAuthorisationUrl(proxy.id) }}
          formats={['pdf']}
          label={{
            text: t('proxies.printForm'),
            name: t('proxies.printFormFor', names),
          }}
        />
      )}
      {canRemove && (
        <ConfirmDialog
          title={t('proxies.removeTitle', names)}
          description={t('proxies.removeDescription', { holder: proxy.holder.name, proxy: proxy.proxy.name })}
          confirmLabel={t('proxies.removeConfirm')}
          onConfirm={() =>
            explainFailure(
              async () => {
                try {
                  await remove.mutateAsync({ id: proxy.id });
                } catch (error) {
                  // Already removed elsewhere: that is what the user wanted.
                  if (problemCode(error) === 'proxies.notFound') return;
                  // Changed meanwhile: show the proxies as they are now, as the reason says.
                  if (isStale(error)) await refresh();
                  throw error;
                }
              },
              (error) => problemMessage(t, error),
            )
          }
          onConfirmed={() => {
            // The row goes away with its buttons: focus moves on (WCAG 2.4.3).
            focusAfterRemoval();
            notify(t('proxies.removed'));
            void refresh();
          }}
          trigger={
            <Button variant="secondary" size="sm" icon={Trash2} aria-label={t('proxies.removeName', names)}>
              {t('proxies.remove')}
            </Button>
          }
        />
      )}
    </div>
  );
}

/** The problem of a proxy that no longer holds, or "none" for screen readers. */
function Problem({ proxy }: { proxy: ProxyResponse }) {
  const { t } = useTranslation('distribution');
  return proxy.problem ? (
    <StatusBadge kind="proxy" value={proxy.problem} />
  ) : (
    <span className="sr-only">{t('proxies.noProblem')}</span>
  );
}

interface ProxiesSectionProps {
  plan: DistributionPlanResponse;
  isAdmin: boolean;
}

/**
 * The edition's pickup proxies (spec: Distribution screens): every comparsa's for Admins, with a
 * comparsa filter; their own for FiringChiefs. Adding and removing while the user may
 * (`canManageProxies`); printing the form while the proxy holds.
 */
export function ProxiesSection({ plan, isAdmin }: ProxiesSectionProps) {
  const { t } = useTranslation('distribution');
  const [search, setSearch] = useSearchParams();
  const addRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  // Inactive comparsas too: their orders, and so their proxies, stay in the edition.
  const comparsasQuery = useListComparsas({ includeInactive: true });
  const comparsas = useMemo(
    () =>
      ((comparsasQuery.data?.data ?? []) as ComparsaResponse[]).map((comparsa) => ({
        id: comparsa.id,
        name: comparsa.name,
      })),
    [comparsasQuery.data],
  );
  const requested = search.get('comparsa') ?? '';
  // Until the comparsas are known, the address's filter is taken as it is (no unfiltered flash).
  const filter = !isAdmin
    ? ''
    : comparsasQuery.isSuccess
      ? knownFilter(
          requested,
          comparsas.map((comparsa) => comparsa.id),
        )
      : requested;
  const proxiesQuery = useListPickupProxies(plan.editionId, filter ? { comparsaId: filter } : undefined);
  const proxies = useMemo(() => (proxiesQuery.data?.data ?? []) as ProxyResponse[], [proxiesQuery.data]);
  const canManage = plan.canManageProxies;
  const focusAfterRemoval = useCallback(() => {
    (addRef.current ?? listRef.current)?.focus();
  }, []);

  const columns = useMemo<DataTableColumn<ProxyResponse>[]>(
    () => [
      {
        id: 'holder',
        header: t('proxies.columns.holder'),
        rowHeader: true,
        cell: (proxy) => proxy.holder.name,
      },
      { id: 'type', header: t('proxies.columns.type'), cell: (proxy) => t(`types.${proxy.type}`) },
      { id: 'proxy', header: t('proxies.columns.proxy'), cell: (proxy) => proxy.proxy.name },
      { id: 'comparsa', header: t('proxies.columns.comparsa'), cell: (proxy) => proxy.comparsaName },
      { id: 'problem', header: t('proxies.columns.problem'), cell: (proxy) => <Problem proxy={proxy} /> },
      {
        id: 'actions',
        header: t('proxies.columns.actions'),
        hideHeader: true,
        pinned: true,
        align: 'end',
        cell: (proxy) => (
          <RowActions proxy={proxy} canRemove={canManage} focusAfterRemoval={focusAfterRemoval} />
        ),
      },
    ],
    [t, canManage, focusAfterRemoval],
  );

  const withProblem = proxies.some((proxy) => proxy.problem !== null);

  return (
    <SectionCard
      title={t('proxies.title')}
      description={t('proxies.description')}
      span="full"
      action={
        comparsas.length > 0 && (
          <ProxySheet
            editionId={plan.editionId}
            comparsas={comparsas}
            triggerRef={addRef}
            hideTrigger={!canManage}
          />
        )
      }
    >
      {!canManage && (
        <AlertBanner severity="info" live={false}>
          {t(plan.editionStatus === 'DRAFT' ? 'proxies.readOnlyDraft' : 'proxies.readOnly')}
        </AlertBanner>
      )}
      {comparsasQuery.isError && (
        <LoadFailure
          error={comparsasQuery.error}
          consequence={t('proxies.comparsasFailed')}
          onRetry={() => comparsasQuery.refetch()}
        />
      )}
      {isAdmin && comparsasQuery.isSuccess && (
        <div className="max-w-xs">
          <FilterSelect
            label={t('proxies.filter')}
            value={filter}
            onChange={(value) => {
              setSearch(withFilter(search, 'comparsa', value), { replace: true });
            }}
            options={[
              { value: '', label: t('proxies.allComparsas') },
              ...comparsas.map((comparsa) => ({ value: comparsa.id, label: comparsa.name })),
            ]}
          />
        </div>
      )}
      {withProblem && <p className="text-help text-muted-foreground">{t('proxies.problemHint')}</p>}
      <p role="status" className="sr-only">
        {proxiesQuery.isSuccess ? t('proxies.count', { count: proxies.length }) : ''}
      </p>
      {proxiesQuery.isError ? (
        <LoadFailure
          error={proxiesQuery.error}
          consequence={t('page.loadFailed')}
          onRetry={() => proxiesQuery.refetch()}
        />
      ) : (
        // Focus lands here when a removed row takes its buttons along and there is no "Add proxy".
        <div ref={listRef} tabIndex={-1} className="outline-none">
          <DataTable
            caption={t('proxies.caption')}
            data={proxies}
            columns={columns}
            getRowId={(proxy) => proxy.id}
            isLoading={proxiesQuery.isPending}
            emptyText={t('proxies.empty')}
            mobileRow={(proxy) => (
              <>
                <span className="font-semibold text-foreground">
                  {t('proxies.collectsFor', { proxy: proxy.proxy.name, holder: proxy.holder.name })}
                </span>
                <span className="flex flex-wrap items-center gap-2 text-help text-muted-foreground">
                  {t(`types.${proxy.type}`)} · {proxy.comparsaName}
                  <Problem proxy={proxy} />
                </span>
                <RowActions proxy={proxy} canRemove={canManage} focusAfterRemoval={focusAfterRemoval} />
              </>
            )}
          />
        </div>
      )}
    </SectionCard>
  );
}
