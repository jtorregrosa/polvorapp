import { useTranslation } from 'react-i18next';
import { PageHeader } from '@/components/app/PageHeader';

/** Placeholder until the registry screens are built (change add-arquebusier-registry, task group 7). */
export function ArquebusierDetailPage() {
  const { t } = useTranslation('registry');
  return <PageHeader title={t('arquebusiers.title')} description={t('arquebusiers.description')} />;
}
