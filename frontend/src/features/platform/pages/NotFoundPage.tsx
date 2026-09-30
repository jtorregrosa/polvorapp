import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/** Shown for any unknown route, inside the shell (spec: Application shell). */
export function NotFoundPage() {
  const { t } = useTranslation();
  useDocumentTitle(t('notFound.title'));

  return (
    <>
      <h1>{t('notFound.title')}</h1>
      <p>{t('notFound.description')}</p>
      <Link to="/">{t('actions.backHome')}</Link>
    </>
  );
}
