import { ShieldX } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { Button } from '@/components/app/Button';
import { EmptyState } from '@/components/app/EmptyState';
import { PageHeader } from '@/components/app/PageHeader';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/** Shown inside the shell for a route the user's role does not allow (spec: Application shell). */
export function ForbiddenPage() {
  const { t } = useTranslation();
  useDocumentTitle(t('forbidden.title'));

  return (
    <>
      <PageHeader title={t('forbidden.title')} />
      <EmptyState
        icon={ShieldX}
        title={t('forbidden.description')}
        action={
          <Button asChild variant="secondary">
            <Link to="/">{t('actions.backHome')}</Link>
          </Button>
        }
      />
    </>
  );
}
