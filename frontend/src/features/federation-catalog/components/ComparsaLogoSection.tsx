import { useTranslation } from 'react-i18next';
import { useRemoveComparsaLogo, useUploadComparsaLogo } from '@/api/generated/comparsas/comparsas';
import type { ComparsaResponse } from '@/api/generated/model';
import { SectionCard } from '@/components/app/SectionCard';
import { logoUrl } from '../logos';
import { LogoUpload } from './LogoUpload';

/** Answers that mean the page shows an outdated comparsa: it is refreshed as well as told. */
const STALE = new Set(['logos.notFound', 'comparsas.notFound']);

interface ComparsaLogoSectionProps {
  comparsa: ComparsaResponse;
  /** Refreshes the comparsa and the lists that show its logo (the sidebar cards included). */
  onChanged: () => Promise<void>;
}

/** Spec "Logo display" for Admins: add, replace and remove the comparsa's logo. */
export function ComparsaLogoSection({ comparsa, onChanged }: ComparsaLogoSectionProps) {
  const { t } = useTranslation('catalog');
  const { mutateAsync: upload } = useUploadComparsaLogo();
  const { mutateAsync: remove } = useRemoveComparsaLogo();

  return (
    <SectionCard title={t('comparsas.logo.section')} description={t('comparsas.logo.help')}>
      <LogoUpload
        texts={{
          label: t('comparsas.logo.label'),
          alt: t('comparsas.logo.alt', { name: comparsa.name }),
          empty: t('comparsas.logo.empty'),
          uploaded: t('comparsas.logo.uploaded'),
          removed: t('comparsas.logo.removed'),
          removeTitle: t('comparsas.logo.removeTitle', { name: comparsa.name }),
          removeDescription: t('comparsas.logo.removeDescription'),
          removeConfirm: t('comparsas.logo.removeConfirm'),
        }}
        photoUrl={logoUrl(comparsa.id, comparsa.logo)}
        upload={(image) => upload({ id: comparsa.id, data: { file: image } })}
        remove={() => remove({ id: comparsa.id })}
        staleCodes={STALE}
        onChanged={onChanged}
      />
    </SectionCard>
  );
}
