import { TriangleAlert } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { Button } from '@/components/app/Button';
import { EmptyState } from '@/components/app/EmptyState';
import { PageHeader } from '@/components/app/PageHeader';
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
      <PageHeader title={t('error.title')} />
      <EmptyState
        icon={TriangleAlert}
        title={t('error.description')}
        action={
          <Button asChild variant="secondary">
            <Link to="/">{t('actions.backHome')}</Link>
          </Button>
        }
      />
    </>
  );
}

/** Fallback when the shell itself fails: the same page in its own main landmark. */
export function RootErrorPage() {
  return (
    <main id="main" className="flex w-full max-w-page flex-col gap-section px-gutter py-section">
      <ErrorPage />
    </main>
  );
}
