import { useTranslation } from 'react-i18next';
import { ClipboardList, Truck } from 'lucide-react';
import { Link, useParams } from 'react-router';
import { useGetEdition } from '@/api/generated/editions/editions';
import type { EditionResponse } from '@/api/generated/model';
import { ApiProblemError } from '@/api/http';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { KeyFacts } from '@/components/app/KeyFacts';
import { PageHeader } from '@/components/app/PageHeader';
import { RecordHeader } from '@/components/app/RecordHeader';
import { SectionGrid } from '@/components/app/SectionGrid';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useSession } from '@/features/identity-access/session';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { useNotice } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { DatesSection, PricesSection } from '../components/EditionSections';
import { MilestonesSection } from '../components/MilestonesSection';
import { ModelsSection } from '../components/ModelsSection';
import { useEditionActions } from '../components/useEditionActions';
import { useEditionDates } from '../dates';
import { useNextWindowText, useOrdersText } from '../editionText';
import { EditionBadges } from './EditionsPage';

function EditionDetail({ edition, isAdmin }: { edition: EditionResponse; isAdmin: boolean }) {
  const { t } = useTranslation('editions');
  const dates = useEditionDates();
  const nextWindow = useNextWindowText();
  const orders = useOrdersText();
  const actions = useEditionActions(edition);
  const [notice] = useNotice();

  return (
    <>
      <RecordHeader
        back={{ to: '/editions', label: t('detail.back') }}
        context={t('detail.context')}
        name={t('detail.name', { year: edition.year })}
        statuses={<EditionBadges edition={edition} />}
        actions={
          <>
            {edition.status !== 'DRAFT' && (
              <Button asChild variant="secondary">
                <Link to={`/editions/${edition.id}/orders`}>
                  <ClipboardList aria-hidden="true" />
                  {t('detail.ordersLink')}
                </Link>
              </Button>
            )}
            {edition.status !== 'DRAFT' && (
              <Button asChild variant="secondary">
                <Link to={`/editions/${edition.id}/distribution`}>
                  <Truck aria-hidden="true" />
                  {t('detail.distributionLink')}
                </Link>
              </Button>
            )}
            {isAdmin && actions.primary}
          </>
        }
        moreActions={isAdmin && actions.items.length > 0 ? actions.items : undefined}
        moreActionsRef={actions.moreActionsRef}
      />
      <NoticeBanner notice={notice} />
      {isAdmin && edition.status === 'DRAFT' && (
        <AlertBanner severity="info" live={false} className="max-w-form">
          {t('detail.draftNotice')}
        </AlertBanner>
      )}
      <KeyFacts
        label={t('detail.keyFacts')}
        items={[
          {
            id: 'festival',
            label: t('detail.festival'),
            value: dates.festival(edition.festivalStartsOn, edition.festivalEndsOn),
          },
          { id: 'nextWindow', label: t('detail.nextWindow'), value: nextWindow(edition) },
          { id: 'orders', label: t('detail.orders'), value: orders(edition) },
        ]}
      />
      <SectionGrid>
        <DatesSection edition={edition} canEdit={isAdmin} />
        <PricesSection edition={edition} canEdit={isAdmin} />
        <ModelsSection edition={edition} canEdit={isAdmin} />
        <MilestonesSection edition={edition} canEdit={isAdmin} />
      </SectionGrid>
      {isAdmin && actions.dialogs}
    </>
  );
}

/**
 * Spec "Editions screens": an edition in read mode, its sections edited in side panels and its
 * lifecycle and orders actions for Admins; FiringChiefs read it. A draft is a 404 for them.
 */
export function EditionDetailPage() {
  const { t } = useTranslation('editions');
  const { id = '' } = useParams();
  const session = useSession();
  const isAdmin = session.account?.role === 'ADMIN';
  const edition = useGetEdition(id, { query: { retry: false } });
  const details = edition.data?.data as EditionResponse | undefined;
  useDocumentTitle(details ? t('detail.name', { year: details.year }) : t('title'));

  if (edition.error instanceof ApiProblemError && edition.error.status === 404) {
    return <NotFoundPage />;
  }
  if (edition.isError && !details) {
    return (
      <>
        <PageHeader title={t('title')} back={{ to: '/editions', label: t('detail.back') }} />
        <LoadFailure
          error={edition.error}
          consequence={t('detail.loadFailed')}
          onRetry={() => edition.refetch()}
        />
      </>
    );
  }
  if (!details) {
    return <PageHeader title={t('title')} back={{ to: '/editions', label: t('detail.back') }} />;
  }
  return (
    <>
      <EditionDetail edition={details} isAdmin={isAdmin} />
      {/* A failed refresh keeps what was loaded, and says it may be outdated. */}
      {edition.isRefetchError && (
        <LoadFailure
          error={edition.error}
          consequence={t('detail.staleFailed')}
          onRetry={() => edition.refetch()}
        />
      )}
    </>
  );
}
