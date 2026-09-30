import { useTranslation } from 'react-i18next';
import { PageHeader } from '@/components/app/PageHeader';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/** Start page. Feature entry points are added here by later changes. */
export function HomePage() {
  const { t } = useTranslation();
  useDocumentTitle(t('home.title'));

  return <PageHeader title={t('home.title')} description={t('home.description')} />;
}
