import { Lock, LockOpen } from 'lucide-react';
import { useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { getGetRegistryLockQueryKey, useSetRegistryLock } from '@/api/generated/registry/registry';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import { useSaveNotice } from '@/components/app/save-notice';
import { useInvalidate } from '@/lib/use-invalidate';
import { problemMessage } from '../problems';
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

/** "Lock registry" or "Unlock registry" for Admins, confirmed with what changes for FiringChiefs. */
export function RegistryLockAction({ lock }: { lock: RegistryLockState }) {
  const { t } = useTranslation('registry');
  const set = useSetRegistryLock();
  const notify = useSaveNotice();
  const refresh = useInvalidate([getGetRegistryLockQueryKey()]);
  const button = useRef<HTMLButtonElement>(null);
  // Only once the lock is known, so the action never offers the opposite of what it does.
  if (!lock.isAdmin || !lock.known) return null;
  const next = !lock.locked;
  const action = next ? 'lock' : 'unlock';
  return (
    <ConfirmDialog
      tone="primary"
      title={t(`lock.${action}.title`)}
      description={t(`lock.${action}.description`)}
      confirmLabel={t(`lock.${action}.action`)}
      onConfirm={() =>
        explainFailure(
          () => set.mutateAsync({ data: { locked: next } }),
          (error) => problemMessage(t, error),
        )
      }
      onConfirmed={() => {
        // The same button stays, now offering the opposite action (WCAG 2.4.3).
        button.current?.focus();
        notify(t(`lock.${action}.done`));
        void refresh();
      }}
      trigger={
        // Rarely used: quieter than the page's other actions; the header's status says the state (UI audit).
        <Button ref={button} variant="quiet" icon={next ? Lock : LockOpen}>
          {t(`lock.${action}.action`)}
        </Button>
      }
    />
  );
}
