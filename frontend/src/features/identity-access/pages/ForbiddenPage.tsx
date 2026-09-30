import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { PageHeader } from '@/components/app/PageHeader';
import { useDocumentTitle } from '@/lib/useDocumentTitle';

/** Shown inside the shell for a route the user's role does not allow (spec: Application shell). */
export function ForbiddenPage() {
  const { t } = useTranslation();
  useDocumentTitle(t('forbidden.title'));

  return (
    <>
      <PageHeader title={t('forbidden.title')} description={t('forbidden.description')} />
      <p className="text-sm">
        <Link to="/" className="font-medium text-primary underline-offset-4 hover:underline">
          {t('actions.backHome')}
        </Link>
      </p>
    </>
  );
}
