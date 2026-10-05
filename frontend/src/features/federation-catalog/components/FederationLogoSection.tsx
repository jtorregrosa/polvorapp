import { useTranslation } from 'react-i18next';
import {
  useGetFederation,
  useRemoveFederationLogo,
  useUploadFederationLogo,
} from '@/api/generated/federation/federation';
import type { FederationResponse } from '@/api/generated/model';
import { SectionCard } from '@/components/app/SectionCard';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { federationLogoUrl } from '../logos';
import { LogoUpload } from './LogoUpload';
import { useSettingsRefresh } from './useSaveSettings';

/** Answers that mean the section shows an outdated logo: it is refreshed as well as told. */
const STALE = new Set(['logos.notFound']);

/**
 * Spec "Federation logo" for Admins, on the Settings page: add, replace and remove the logo printed
 * on the Federation's documents. It is uploaded at run time and never part of the repository.
 */
export function FederationLogoSection() {
  const { t } = useTranslation('catalog');
  const settings = useGetFederation();
  const { mutateAsync: upload } = useUploadFederationLogo();
  const { mutateAsync: remove } = useRemoveFederationLogo();
  // A logo change also changes the settings' version: both are reloaded.
  const refresh = useSettingsRefresh();
  const logo = (settings.data?.data as FederationResponse | undefined)?.logo;

  const content = () => {
    if (settings.isError && logo === undefined) {
      return (
        <LoadFailure
          error={settings.error}
          consequence={t('federation.logo.loadFailed')}
          onRetry={() => settings.refetch()}
        />
      );
    }
    if (logo === undefined) {
      return (
        <p role="status" className="text-help text-muted-foreground">
          {t('federation.logo.loading')}
        </p>
      );
    }
    return (
      <>
        {/* A failed refresh (e.g. after a change) keeps what was loaded, and says it may be outdated. */}
        {settings.isRefetchError && (
          <LoadFailure
            error={settings.error}
            consequence={t('federation.logo.staleFailed')}
            onRetry={() => settings.refetch()}
          />
        )}
        <LogoUpload
          texts={{
            label: t('federation.logo.label'),
            alt: t('federation.logo.alt'),
            uploaded: t('federation.logo.uploaded'),
            removed: t('federation.logo.removed'),
            removeTitle: t('federation.logo.removeTitle'),
            removeDescription: t('federation.logo.removeDescription'),
            removeConfirm: t('federation.logo.removeConfirm'),
          }}
          photoUrl={federationLogoUrl(logo)}
          upload={(image) => upload({ data: { file: image } })}
          remove={() => remove()}
          staleCodes={STALE}
          onChanged={refresh}
        />
      </>
    );
  };

  return (
    <SectionCard title={t('federation.logo.section')} description={t('federation.logo.help')}>
      {content()}
    </SectionCard>
  );
}
