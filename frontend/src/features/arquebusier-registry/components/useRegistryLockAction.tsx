import { Lock, LockOpen } from 'lucide-react';
import { useState, type ReactNode, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { getGetRegistryLockQueryKey, useSetRegistryLock } from '@/api/generated/registry/registry';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import type { MoreAction } from '@/components/app/MoreActionsMenu';
import { useSaveNotice } from '@/components/app/save-notice';
import { useInvalidate } from '@/lib/use-invalidate';
import { problemMessage } from '../problems';
import type { RegistryLockState } from '../registryLock';

/**
 * "Lock registry" or "Unlock registry" for Admins, an item of the page's "More actions" (UI audit:
 * rarely used), confirmed with what changes for FiringChiefs. Focus returns to the menu's button.
 */
export function useRegistryLockAction(
  lock: RegistryLockState,
  returnFocus: RefObject<HTMLElement | null>,
): { item?: MoreAction; dialog: ReactNode } {
  const { t } = useTranslation('registry');
  const set = useSetRegistryLock();
  const notify = useSaveNotice();
  const refresh = useInvalidate([getGetRegistryLockQueryKey()]);
  const [open, setOpen] = useState(false);
  // Only once the lock is known, so the action never offers the opposite of what it does.
  if (!lock.isAdmin || !lock.known) return { dialog: null };
  const next = !lock.locked;
  const action = next ? 'lock' : 'unlock';
  return {
    item: {
      id: 'registry-lock',
      label: t(`lock.${action}.action`),
      icon: next ? Lock : LockOpen,
      onSelect: () => {
        setOpen(true);
      },
    },
    dialog: (
      <ConfirmDialog
        tone="primary"
        open={open}
        onOpenChange={setOpen}
        returnFocus={returnFocus}
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
          returnFocus.current?.focus();
          notify(t(`lock.${action}.done`));
          void refresh();
        }}
      />
    ),
  };
}
