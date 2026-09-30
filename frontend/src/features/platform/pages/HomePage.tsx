import { useTranslation } from 'react-i18next';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/** Start page. Feature entry points are added here by later changes. */
export function HomePage() {
  const { t } = useTranslation();
  useDocumentTitle(t('home.title'));

  return (
    <>
      <h1>{t('home.title')}</h1>
      <p>{t('home.description')}</p>
    </>
  );
}
