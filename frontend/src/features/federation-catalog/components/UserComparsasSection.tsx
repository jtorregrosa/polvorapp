import { useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useListComparsas } from '@/api/generated/comparsas/comparsas';
import {
  useAssignFiringChief,
  useListFiringChiefComparsas,
  useUnassignFiringChief,
} from '@/api/generated/firing-chief-assignments/firing-chief-assignments';
import type { ComparsaResponse, UserResponse } from '@/api/generated/model';
import type { DataTableColumn } from '@/components/app/DataTable';
import { StatusBadge } from '@/components/app/StatusBadge';
import { CategoryTag } from '@/components/app/Tag';
import { AssignmentList, type AssignmentCandidate, type AssignmentListText } from './AssignmentList';
import { useRefreshAssignments } from './useRefreshAssignments';

const comparsaId = (comparsa: ComparsaResponse): string => comparsa.id;
const comparsaName = (comparsa: ComparsaResponse): string => comparsa.name;

/**
 * The comparsas of a user, on the identity user page (Admins only; design D8). Shown for every
 * FiringChief, and for an Admin who still has assignments, saying they have no effect. Candidates
 * are active comparsas not yet assigned (the comparsa list shows only active ones by default).
 */
export function UserComparsasSection({ user }: { user: UserResponse }) {
  const { t } = useTranslation('catalog');
  const { mutateAsync: assign } = useAssignFiringChief();
  const { mutateAsync: unassign } = useUnassignFiringChief();
  const refresh = useRefreshAssignments();
  const assigned = useListFiringChiefComparsas(user.id);
  const rows = useMemo(() => (assigned.data?.data ?? []) as ComparsaResponse[], [assigned.data]);
  // An Admin without assignments shows nothing, so the candidates are not needed.
  const shown = user.role !== 'ADMIN' || rows.length > 0;
  const active = useListComparsas(undefined, { query: { enabled: shown } });

  const candidates = useMemo<AssignmentCandidate[]>(() => {
    const taken = new Set(rows.map((comparsa) => comparsa.id));
    return ((active.data?.data ?? []) as ComparsaResponse[])
      .filter((comparsa) => comparsa.active && !taken.has(comparsa.id))
      .map((comparsa) => ({ id: comparsa.id, name: comparsa.name, label: comparsa.name }));
  }, [active.data, rows]);

  const columns = useMemo<DataTableColumn<ComparsaResponse>[]>(
    () => [
      { id: 'name', header: t('comparsas.columns.name'), rowHeader: true, cell: (comparsa) => comparsa.name },
      {
        id: 'side',
        header: t('comparsas.columns.side'),
        cell: (comparsa) => <CategoryTag category="side" value={comparsa.side} />,
      },
      {
        id: 'status',
        header: t('comparsas.columns.status'),
        cell: (comparsa) => <StatusBadge kind="catalog" value={comparsa.active ? 'ACTIVE' : 'INACTIVE'} />,
      },
    ],
    [t],
  );

  const addBlocked =
    user.role === 'ADMIN'
      ? t('userComparsas.adminNoEffect')
      : user.status === 'DEACTIVATED'
        ? t('errors.assignments.userDeactivated')
        : undefined;

  const text = useMemo<AssignmentListText>(
    () => ({
      title: t('userComparsas.title'),
      caption: t('userComparsas.caption', { name: user.name }),
      description: t('userComparsas.description'),
      emptyText: t('userComparsas.empty'),
      addLabel: t('userComparsas.addLabel'),
      add: t('userComparsas.add'),
      addAction: t('userComparsas.addAction'),
      addTitle: t('userComparsas.addTitle'),
      noCandidates: t('userComparsas.noCandidates'),
      addBlocked,
      added: (name) => t('userComparsas.added', { name }),
      removed: (name) => t('userComparsas.removed', { name }),
      remove: (name) => t('userComparsas.remove', { name }),
      removeShort: t('userComparsas.removeShort'),
      removeTitle: (name) => t('userComparsas.removeTitle', { name }),
      removeDescription: t('userComparsas.removeDescription'),
    }),
    [addBlocked, t, user.name],
  );

  const onAdd = useCallback(
    (comparsaId: string) => assign({ id: comparsaId, userId: user.id }),
    [assign, user.id],
  );
  const onRemove = useCallback(
    (comparsa: ComparsaResponse) => unassign({ id: comparsa.id, userId: user.id }),
    [unassign, user.id],
  );

  // An Admin's assignments only matter if they have some: otherwise there is nothing to say.
  if (user.role === 'ADMIN' && (!assigned.isSuccess || !shown)) {
    return null;
  }

  return (
    <AssignmentList
      text={text}
      rows={rows}
      columns={columns}
      getRowId={comparsaId}
      getRowName={comparsaName}
      describeRow={(comparsa) => (
        <>
          <span className="font-semibold text-foreground">{comparsa.name}</span>
          <span className="flex flex-wrap items-center gap-2 text-help text-muted-foreground">
            {/* Each value with its term for screen readers (WCAG 1.3.1). */}
            <span>
              <span className="sr-only">{t('comparsas.columns.side')}: </span>
              <CategoryTag category="side" value={comparsa.side} />
            </span>
            <span>
              <span className="sr-only">{t('comparsas.columns.status')}: </span>
              <StatusBadge kind="catalog" value={comparsa.active ? 'ACTIVE' : 'INACTIVE'} />
            </span>
          </span>
        </>
      )}
      isLoading={assigned.isPending || active.isPending}
      error={assigned.error ?? active.error ?? undefined}
      candidates={candidates}
      onAdd={onAdd}
      onRemove={onRemove}
      onChanged={refresh}
    />
  );
}
