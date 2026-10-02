import { SearchX } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { Button } from '@/components/app/Button';
import { EmptyState } from '@/components/app/EmptyState';
import { PageHeader } from '@/components/app/PageHeader';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/** Shown for any unknown route, inside the shell (spec: Application shell). */
export function NotFoundPage() {
  const { t } = useTranslation();
  useDocumentTitle(t('notFound.title'));

  return (
    <>
      <PageHeader title={t('notFound.title')} />
      <EmptyState
        icon={SearchX}
        title={t('notFound.description')}
        action={
          <Button asChild variant="secondary">
            <Link to="/">{t('actions.backHome')}</Link>
          </Button>
        }
      />
    </>
  );
}
