import { useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useAssignFiringChief,
  useUnassignFiringChief,
} from '@/api/generated/firing-chief-assignments/firing-chief-assignments';
import type { ComparsaResponse, FiringChiefResponse, UserResponse } from '@/api/generated/model';
import { useListUsers } from '@/api/generated/users/users';
import type { DataTableColumn } from '@/components/app/DataTable';
import { StatusBadge } from '@/components/app/StatusBadge';
import { AssignmentList, type AssignmentCandidate, type AssignmentListText } from './AssignmentList';
import { useRefreshAssignments } from './useRefreshAssignments';

/**
 * The FiringChiefs of a comparsa, on its page (Admins only). Candidates are FiringChiefs who are
 * not deactivated and not yet assigned; the list of users comes from the identity API (the one
 * dependency on identity-access data besides the user page's section, design D8).
 */
const chiefId = (chief: FiringChiefResponse): string => chief.userId;
const chiefName = (chief: FiringChiefResponse): string => chief.name;

export function FiringChiefsSection({
  comparsa,
  chiefs,
  isLoading,
  error,
}: {
  comparsa: ComparsaResponse;
  chiefs: readonly FiringChiefResponse[];
  isLoading: boolean;
  /** The comparsa's FiringChiefs could not be loaded. */
  error?: unknown;
}) {
  const { t } = useTranslation('catalog');
  const { mutateAsync: assign } = useAssignFiringChief();
  const { mutateAsync: unassign } = useUnassignFiringChief();
  const refresh = useRefreshAssignments();
  const users = useListUsers({ role: 'FIRING_CHIEF' });

  const candidates = useMemo<AssignmentCandidate[]>(() => {
    const assigned = new Set(chiefs.map((chief) => chief.userId));
    return ((users.data?.data ?? []) as UserResponse[])
      .filter(
        (user) => user.role === 'FIRING_CHIEF' && user.status !== 'DEACTIVATED' && !assigned.has(user.id),
      )
      .map((user) => ({ id: user.id, name: user.name, label: `${user.name} (${user.email})` }));
  }, [chiefs, users.data]);

  const columns = useMemo<DataTableColumn<FiringChiefResponse>[]>(
    () => [
      { id: 'name', header: t('firingChiefs.columns.name'), cell: (chief) => chief.name },
      { id: 'email', header: t('firingChiefs.columns.email'), cell: (chief) => chief.email },
      {
        id: 'status',
        header: t('firingChiefs.columns.status'),
        cell: (chief) => <StatusBadge kind="user" value={chief.status} />,
      },
    ],
    [t],
  );

  const text = useMemo<AssignmentListText>(
    () => ({
      title: t('firingChiefs.title'),
      description: t('firingChiefs.description'),
      emptyText: t('firingChiefs.empty'),
      addLabel: t('firingChiefs.addLabel'),
      add: t('firingChiefs.add'),
      noCandidates: t('firingChiefs.noCandidates'),
      addBlocked: comparsa.active ? undefined : t('firingChiefs.inactiveComparsa'),
      added: (name) => t('firingChiefs.added', { name }),
      removed: (name) => t('firingChiefs.removed', { name }),
      remove: (name) => t('firingChiefs.remove', { name }),
      removeShort: t('firingChiefs.removeShort'),
      removeTitle: (name) => t('firingChiefs.removeTitle', { name }),
      removeDescription: t('firingChiefs.removeDescription'),
    }),
    [comparsa.active, t],
  );

  const onAdd = useCallback((userId: string) => assign({ id: comparsa.id, userId }), [assign, comparsa.id]);
  const onRemove = useCallback(
    (chief: FiringChiefResponse) => unassign({ id: comparsa.id, userId: chief.userId }),
    [unassign, comparsa.id],
  );

  return (
    <AssignmentList
      text={text}
      rows={chiefs}
      columns={columns}
      getRowId={chiefId}
      getRowName={chiefName}
      isLoading={isLoading || users.isPending}
      error={error ?? users.error ?? undefined}
      candidates={candidates}
      onAdd={onAdd}
      onRemove={onRemove}
      onChanged={refresh}
    />
  );
}
