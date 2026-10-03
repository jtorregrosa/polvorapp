import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { useGetComparsaOrdersOverview } from '@/api/generated/comparsa-orders/comparsa-orders';
import {
  getDownloadComparsaListUrl,
  getDownloadRecipientExportUrl,
  useGetExportCatalog,
} from '@/api/generated/exports/exports';
import type { ExportCatalogResponse, OverviewResponse, OverviewRowResponse } from '@/api/generated/model';
import { ApiProblemError } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { PageHeader } from '@/components/app/PageHeader';
import { SectionCard } from '@/components/app/SectionCard';
import { SectionGrid } from '@/components/app/SectionGrid';
import { StatusBadge } from '@/components/app/StatusBadge';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { DownloadButtons } from '../components/DownloadButtons';

const RECIPIENTS = ['powder-supplier', 'rental-company', 'arms-authority'] as const;
type Recipient = (typeof RECIPIENTS)[number];

const urls = (url: (format: string) => string) => ({ xlsx: url('xlsx'), pdf: url('pdf') });

type MissingStatus = 'NOT_PREPARED' | 'DRAFT' | 'SUBMITTED' | 'RETURNED';

/** Why a comparsa is left out of the recipient exports, or null when its order is validated. */
function missingStatus(row: OverviewRowResponse): MissingStatus | null {
  if (row.status === 'VALIDATED') return null;
  return row.status ?? 'NOT_PREPARED';
}

/** The comparsas left out of the recipient exports: no order, or an order not validated yet. */
function Missing({ rows }: { rows: readonly OverviewRowResponse[] }) {
  const { t } = useTranslation('exports');
  const missing = rows.flatMap((row) => {
    const status = missingStatus(row);
    return status ? [{ row, status }] : [];
  });
  if (missing.length === 0) return null;
  return (
    <AlertBanner severity="warning" live={false} title={t('missing.title')}>
      <p>{t('missing.body', { count: missing.length })}</p>
      {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
      <ul role="list" className="mt-1 flex flex-col gap-1">
        {missing.map(({ row, status }) => (
          <li key={row.comparsa.id}>
            {t('missing.item', { comparsa: row.comparsa.name, status: t(`missing.status.${status}`) })}
          </li>
        ))}
      </ul>
    </AlertBanner>
  );
}

/**
 * The exports of an edition (spec: Exports screens; design D9), for Admins: the provisional notice,
 * the comparsas the recipient exports leave out, each recipient export and each comparsa's list.
 */
export function ExportsPage() {
  const { t } = useTranslation('exports');
  const { editionId = '' } = useParams();
  const catalog = useGetExportCatalog(editionId);
  const overview = useGetComparsaOrdersOverview({ editionId });
  const definitions = (catalog.data?.data as ExportCatalogResponse | undefined)?.definitions;
  const data = overview.data?.data as OverviewResponse | undefined;
  const year = data?.edition?.year;
  useDocumentTitle(year ? t('page.title', { year }) : t('page.link'));

  const failed = catalog.isError ? catalog.error : overview.isError ? overview.error : undefined;
  if (failed instanceof ApiProblemError && failed.status === 404) {
    return <NotFoundPage />;
  }

  const provisional = definitions?.some((definition) => definition.provisional) ?? false;
  const offered = new Set(definitions?.map((definition) => definition.name));
  const prepared = data?.rows.filter((row) => row.orderId) ?? [];

  return (
    <>
      <PageHeader
        title={year ? t('page.title', { year }) : t('page.link')}
        description={t('page.description')}
        back={{
          to: `/editions/${editionId}/orders`,
          label: year ? t('page.back', { year }) : t('page.backToOrders'),
        }}
      />
      {failed && (
        <LoadFailure
          error={failed}
          onRetry={() => {
            void catalog.refetch();
            void overview.refetch();
          }}
        />
      )}
      {provisional && (
        <AlertBanner severity="info" live={false} title={t('provisional.title')}>
          {t('provisional.body')}
        </AlertBanner>
      )}
      {data && <Missing rows={data.rows} />}
      {definitions && (
        <SectionGrid>
          {RECIPIENTS.filter((name) => offered.has(name)).map((name: Recipient) => (
            <SectionCard
              key={name}
              title={t(`recipients.${name}.title`)}
              description={t(`recipients.${name}.description`)}
            >
              <DownloadButtons
                what={t(`recipients.${name}.title`)}
                urls={urls((format) => getDownloadRecipientExportUrl(editionId, name, format))}
              />
            </SectionCard>
          ))}
        </SectionGrid>
      )}
      {data && (
        <SectionGrid>
          <SectionCard title={t('comparsas.title')} description={t('comparsas.description')} span="full">
            {prepared.length === 0 ? (
              <p className="text-muted-foreground">{t('comparsas.empty')}</p>
            ) : (
              // eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`.
              <ul role="list" className="flex flex-col gap-4">
                {prepared.map((row) => {
                  const draft = row.status !== 'VALIDATED';
                  const what = t(draft ? 'download.draftListOf' : 'download.listOf', {
                    comparsa: row.comparsa.name,
                  });
                  return (
                    <li key={row.comparsa.id} className="flex flex-wrap items-start justify-between gap-2">
                      <span className="flex min-w-0 flex-wrap items-center gap-2">
                        <span className="font-semibold break-words">{row.comparsa.name}</span>
                        {row.status && <StatusBadge kind="order" value={row.status} />}
                      </span>
                      <DownloadButtons
                        what={what}
                        urls={urls((format) =>
                          getDownloadComparsaListUrl(editionId, row.comparsa.id, format),
                        )}
                      />
                    </li>
                  );
                })}
              </ul>
            )}
          </SectionCard>
        </SectionGrid>
      )}
    </>
  );
}
