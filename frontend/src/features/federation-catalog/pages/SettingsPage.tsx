import { useTranslation } from 'react-i18next';
import { useGetFederationSettings } from '@/api/generated/settings/settings';
import type { FederationSettingsResponse } from '@/api/generated/model';
import { PageHeader } from '@/components/app/PageHeader';
import { SectionGrid } from '@/components/app/SectionGrid';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { FederationLogoSection } from '../components/FederationLogoSection';
import {
  CalendarSection,
  EmailsSection,
  IdentitySection,
  OrdersSection,
} from '../components/SettingsSections';

/**
 * Spec "Settings screen" (add-federation-settings, design D5): the Federation's settings for Admins,
 * one section per group, each edited in its own panel and saved on its own. Later groups join the
 * grid as further sections.
 */
export function SettingsPage() {
  const { t } = useTranslation('catalog');
  useDocumentTitle(t('settings.title'));
  const query = useGetFederationSettings();
  const settings = query.data?.data as FederationSettingsResponse | undefined;

  return (
    <>
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      {query.isError && !settings && (
        <LoadFailure
          error={query.error}
          consequence={t('settings.loadFailed')}
          onRetry={() => query.refetch()}
        />
      )}
      {/* A failed refresh keeps what was loaded, and says it may be outdated. */}
      {settings && query.isRefetchError && (
        <LoadFailure
          error={query.error}
          consequence={t('settings.staleFailed')}
          onRetry={() => query.refetch()}
        />
      )}
      <SectionGrid>
        {settings && <IdentitySection settings={settings} />}
        {/* The logo has its own request: it stays usable when the settings cannot be loaded. */}
        <FederationLogoSection />
        {settings && <EmailsSection settings={settings} />}
        {settings && <OrdersSection settings={settings} />}
        {settings && <CalendarSection settings={settings} />}
      </SectionGrid>
      {/* Mounted from the start, so its text is announced when it changes. */}
      <p role="status" className="text-help text-muted-foreground">
        {!settings && !query.isError ? t('settings.loading') : ''}
      </p>
    </>
  );
}
