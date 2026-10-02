import { useTranslation } from 'react-i18next';
import { useRemoveComparsaLogo, useUploadComparsaLogo } from '@/api/generated/comparsas/comparsas';
import type { ComparsaResponse } from '@/api/generated/model';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { PhotoUploadFailure } from '@/components/app/photo-image';
import { PhotoUpload } from '@/components/app/PhotoUpload';
import { SectionCard } from '@/components/app/SectionCard';
import { LOGO_RULES, logoUrl } from '../logos';
import { logoProblemMessage, problemCode } from '../problems';

/** Answers that mean the page shows an outdated comparsa: it is refreshed as well as told. */
const STALE = new Set(['logos.notFound', 'comparsas.notFound']);

interface ComparsaLogoSectionProps {
  comparsa: ComparsaResponse;
  /** Refreshes the comparsa and the lists that show its logo (the sidebar cards included). */
  onChanged: () => Promise<void>;
}

/**
 * Spec "Logo display" for Admins: add, replace and remove the comparsa's logo. The image is chosen
 * and cropped freely in the browser (the whole image selected at first), kept as PNG with its
 * transparency, and the API's reasons are shown translated in the crop dialog or the confirmation.
 */
export function ComparsaLogoSection({ comparsa, onChanged }: ComparsaLogoSectionProps) {
  const { t } = useTranslation('catalog');
  const { mutateAsync: upload } = useUploadComparsaLogo();
  const { mutateAsync: remove } = useRemoveComparsaLogo();

  /** After a successful change: a failed refresh shows the page's own load failure, not a failed change. */
  const refreshQuietly = async () => {
    try {
      await onChanged();
    } catch {
      // The page's query reports its own failure.
    }
  };

  const uploadLogo = async (image: Blob) => {
    try {
      await upload({ id: comparsa.id, data: { file: image } });
    } catch (error) {
      if (STALE.has(problemCode(error) ?? '')) await refreshQuietly();
      throw new PhotoUploadFailure(logoProblemMessage(t, error));
    }
    await refreshQuietly();
  };

  const removeLogo = async () => {
    try {
      await remove({ id: comparsa.id });
    } catch (error) {
      // Already removed elsewhere: that is what the Admin wanted.
      if (problemCode(error) === 'logos.notFound') {
        await refreshQuietly();
        return;
      }
      if (STALE.has(problemCode(error) ?? '')) await refreshQuietly();
      throw new ConfirmFailure(logoProblemMessage(t, error));
    }
    await refreshQuietly();
  };

  return (
    <SectionCard title={t('comparsas.logo.section')} description={t('comparsas.logo.help')}>
      <PhotoUpload
        label={t('comparsas.logo.label')}
        photoUrl={logoUrl(comparsa.id, comparsa.logo)}
        photoAlt={t('comparsas.logo.alt', { name: comparsa.name })}
        emptyText={t('comparsas.logo.empty')}
        uploadedText={t('comparsas.logo.uploaded')}
        removedText={t('comparsas.logo.removed')}
        subject="logo"
        onUpload={uploadLogo}
        removal={{
          title: t('comparsas.logo.removeTitle', { name: comparsa.name }),
          description: t('comparsas.logo.removeDescription'),
          confirmLabel: t('comparsas.logo.removeConfirm'),
          onRemove: removeLogo,
        }}
        {...LOGO_RULES}
      />
    </SectionCard>
  );
}
