import { useQueryClient } from '@tanstack/react-query';
import { Download, Eraser } from 'lucide-react';
import { useState, type ReactNode, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import type { UserResponse } from '@/api/generated/model';
import { getExportUserDataUrl, useEraseUserData } from '@/api/generated/privacy/privacy';
import { responseData } from '@/api/http';
import type { MoreAction } from '@/components/app/RecordHeader';
import { useFormatters } from '@/lib/format';
import { erasureOutcome, forgetAuditPages } from '../erasure-outcome';
import { downloadPersonalData } from '../privacy-download';
import { PrivacyRequestDialog } from './PrivacyRequestDialog';

export interface UserPrivacyOptions {
  /** The user's name as shown, for the erasure's title. */
  name: string;
  /** Where focus returns when a dialog is cancelled: the "More actions" button. */
  returnFocus: RefObject<HTMLElement | null>;
  /** Shows an outcome once a dialog has closed. */
  announce: (text: string) => void;
  /** Reloads the user after an erasure. */
  onErased: () => Promise<void>;
}

/**
 * "Download personal data" and "Erase personal data" for a user's "More actions" (spec: GDPR
 * request screens; design D12), with their dialogs. None for an erased user.
 */
export function useUserPrivacyActions(
  user: UserResponse,
  { name, returnFocus, announce, onErased }: UserPrivacyOptions,
): { items: MoreAction[]; dialogs: ReactNode } {
  const { t } = useTranslation('privacy');
  const format = useFormatters();
  const erase = useEraseUserData();
  const queryClient = useQueryClient();
  const [dialog, setDialog] = useState<'export' | 'erase'>();

  if (user.status === 'ERASED') return { items: [], dialogs: null };

  const close = (open: boolean) => {
    if (!open) setDialog(undefined);
  };

  const items: MoreAction[] = [
    {
      id: 'exportPersonalData',
      label: t('export.userAction'),
      icon: Download,
      onSelect: () => {
        setDialog('export');
      },
    },
    {
      id: 'erasePersonalData',
      label: t('erase.userAction'),
      icon: Eraser,
      destructive: true,
      onSelect: () => {
        setDialog('erase');
      },
    },
  ];

  const dialogs = (
    <>
      <PrivacyRequestDialog
        open={dialog === 'export'}
        onOpenChange={close}
        returnFocus={returnFocus}
        tone="primary"
        title={t('export.title')}
        description={t('export.description')}
        confirmLabel={t('export.confirm')}
        onRequest={(reference) => downloadPersonalData(getExportUserDataUrl(user.id), { reference })}
        onDone={() => {
          announce(t('export.done'));
        }}
      />
      <PrivacyRequestDialog
        open={dialog === 'erase'}
        onOpenChange={close}
        returnFocus={returnFocus}
        tone="destructive"
        title={t('erase.title', { name })}
        description={t('erase.userDescription')}
        confirmLabel={t('erase.confirm')}
        notes={<p>{t('erase.warnings.irreversible')}</p>}
        onRequest={async (reference) =>
          responseData(await erase.mutateAsync({ id: user.id, data: { reference } }))
        }
        onDone={(result) => {
          erase.reset();
          forgetAuditPages(queryClient);
          announce(erasureOutcome(t, format.list, result));
          void onErased();
        }}
      />
    </>
  );

  return { items, dialogs };
}
