import { useTranslation } from 'react-i18next';
import { AlertBanner } from '@/components/app/AlertBanner';
import type { RegistryLockState } from '../registryLock';

/** While the registry is locked, says what that means for the caller (spec: Registry lock screens). */
export function RegistryLockNotice({ lock }: { lock: RegistryLockState }) {
  const { t } = useTranslation('registry');
  if (!lock.locked) return null;
  return (
    // A lasting state when the page opens; announced when it arrives as a refused change.
    <AlertBanner
      severity="info"
      live={lock.lockedMeanwhile}
      className="max-w-form"
      title={t('lock.notice.title')}
    >
      {lock.isAdmin ? t('lock.notice.admin') : t('lock.notice.firingChief')}
    </AlertBanner>
  );
}
