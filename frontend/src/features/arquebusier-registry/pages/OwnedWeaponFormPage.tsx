import { useTranslation } from 'react-i18next';
import { PageHeader } from '@/components/app/PageHeader';

/** Placeholder until the registry screens are built (change add-arquebusier-registry, task group 7). */
export function OwnedWeaponFormPage() {
  const { t } = useTranslation('registry');
  return (
    <PageHeader title={t('ownedWeapons.form.newTitle')} description={t('ownedWeapons.form.description')} />
  );
}
