import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/**
 * Shown when a page fails to render. Never shows error details: they may contain data and are
 * meaningless to users; React Router still reports the error to the console.
 */
export function ErrorPage() {
  const { t } = useTranslation();
  useDocumentTitle(t('error.title'));

  return (
    <>
      <h1>{t('error.title')}</h1>
      <p>{t('error.description')}</p>
      <Link to="/">{t('actions.backHome')}</Link>
    </>
  );
}

/** Fallback when the shell itself fails: the same page in its own main landmark. */
export function RootErrorPage() {
  return (
    <main id="main" className="shell-main">
      <ErrorPage />
    </main>
  );
}
