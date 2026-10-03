import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { useGetDistributionPlan } from '@/api/generated/distribution/distribution';
import type { DistributionPlanResponse } from '@/api/generated/model';
import { ApiProblemError } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { PageHeader } from '@/components/app/PageHeader';
import { SectionGrid } from '@/components/app/SectionGrid';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useSession } from '@/features/identity-access/session';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { DistributionDaySection } from '../components/DistributionDaySection';
import { ProxiesSection } from '../components/ProxiesSection';

function Distribution({ plan, stale }: { plan: DistributionPlanResponse; stale: ReactNode }) {
  const { t } = useTranslation('distribution');
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  return (
    <>
      <PageHeader
        title={t('page.title', { year: plan.editionYear })}
        description={t('page.description')}
        back={{ to: `/editions/${plan.editionId}`, label: t('page.back', { year: plan.editionYear }) }}
      />
      {stale}
      {plan.editionStatus !== 'IN_PROGRESS' && (
        <AlertBanner severity="info" live={false} className="max-w-form">
          {t(`page.notInProgress.${plan.editionStatus}`)}
        </AlertBanner>
      )}
      <SectionGrid>
        <DistributionDaySection plan={plan} type="POWDER" isAdmin={isAdmin} />
        <DistributionDaySection plan={plan} type="WEAPONS" isAdmin={isAdmin} />
        <ProxiesSection plan={plan} isAdmin={isAdmin} />
      </SectionGrid>
    </>
  );
}

/**
 * Spec "Distribution screens": an edition's powder and weapons days with their slots, and its pickup
 * proxies. The server decides what each user sees and may change (BR-12); a draft is a 404 for
 * FiringChiefs.
 */
export function DistributionPage() {
  const { t } = useTranslation('distribution');
  const { t: tCommon } = useTranslation('common');
  const { editionId = '' } = useParams();
  const query = useGetDistributionPlan(editionId, { query: { retry: false } });
  const plan = query.data?.data as DistributionPlanResponse | undefined;
  useDocumentTitle(plan ? t('page.title', { year: plan.editionYear }) : tCommon('nav.distribution'));

  if (query.error instanceof ApiProblemError && query.error.status === 404) {
    return <NotFoundPage />;
  }
  if (!plan) {
    return (
      <>
        <PageHeader title={tCommon('nav.distribution')} />
        {query.isPending && (
          <p role="status" className="text-muted-foreground">
            {t('page.loading')}
          </p>
        )}
        {query.isError && (
          <LoadFailure
            error={query.error}
            consequence={t('page.loadFailed')}
            onRetry={() => query.refetch()}
          />
        )}
      </>
    );
  }
  return (
    <Distribution
      plan={plan}
      stale={
        // A failed refresh keeps what was loaded, and says it may be outdated, under the header.
        query.isRefetchError && (
          <LoadFailure
            error={query.error}
            consequence={t('page.staleFailed')}
            onRetry={() => query.refetch()}
          />
        )
      }
    />
  );
}
