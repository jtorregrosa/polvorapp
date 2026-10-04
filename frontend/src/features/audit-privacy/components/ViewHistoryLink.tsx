import { History } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { Button } from '@/components/app/Button';
import { useSession } from '@/features/identity-access/session';

export interface ViewHistoryLinkProps {
  /** The record's audit entity type, e.g. `Arquebusier`. */
  entityType: string;
  entityId: string;
}

/**
 * "View history" in a record's header (spec: Audit log screens): the audit log filtered by the
 * record. Only Admins see it; the audit log refuses everyone else anyway.
 */
export function ViewHistoryLink({ entityType, entityId }: ViewHistoryLinkProps) {
  const { t } = useTranslation('audit');
  const isAdmin = useSession().account?.role === 'ADMIN';
  if (!isAdmin) return null;

  const search = new URLSearchParams({ entityType, entityId });
  return (
    <Button asChild variant="secondary">
      <Link to={`/audit-log?${search.toString()}`}>
        <History aria-hidden="true" />
        {t('viewHistory')}
      </Link>
    </Button>
  );
}
