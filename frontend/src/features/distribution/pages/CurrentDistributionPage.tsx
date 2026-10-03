import { CalendarDays } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, Navigate } from 'react-router';
import { useGetCurrentEdition } from '@/api/generated/editions/editions';
import type { CurrentEditionResponse } from '@/api/generated/model';
import { Button } from '@/components/app/Button';
import { EmptyState } from '@/components/app/EmptyState';
import { PageHeader } from '@/components/app/PageHeader';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/**
 * The "Distribution" navigation entry (spec: Distribution screens): opens the distribution of the
 * edition in progress, or says there is none and links to the editions.
 */
export function CurrentDistributionPage() {
  const { t } = useTranslation('distribution');
  const { t: tCommon } = useTranslation('common');
  useDocumentTitle(tCommon('nav.distribution'));
  const current = useGetCurrentEdition();
  const edition = (current.data?.data as CurrentEditionResponse | undefined)?.edition;

  if (edition) {
    return <Navigate to={`/editions/${edition.id}/distribution`} replace />;
  }
  return (
    <>
      <PageHeader title={tCommon('nav.distribution')} />
      {current.isPending && (
        <p role="status" className="text-muted-foreground">
          {t('page.loading')}
        </p>
      )}
      {current.isError && (
        <LoadFailure
          error={current.error}
          consequence={t('page.loadFailed')}
          onRetry={() => current.refetch()}
        />
      )}
      {current.isSuccess && (
        <EmptyState
          icon={CalendarDays}
          title={t('page.noCurrent.title')}
          description={t('page.noCurrent.body')}
          action={
            <Button asChild variant="secondary">
              <Link to="/editions">{t('page.noCurrent.link')}</Link>
            </Button>
          }
        />
      )}
    </>
  );
}
