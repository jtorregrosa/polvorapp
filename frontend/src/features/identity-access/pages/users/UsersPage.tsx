import { UserPlus } from 'lucide-react';
import { useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useListAssignments } from '@/api/generated/firing-chief-assignments/firing-chief-assignments';
import { useListUsers } from '@/api/generated/users/users';
import {
  UserRole,
  UserStatus,
  type AssignmentResponse,
  type ListUsersParams,
  type UserResponse,
} from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { FilterBar } from '@/components/app/FilterBar';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { CategoryTag } from '@/components/app/Tag';
import { breakable } from '@/components/app/breakable';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { formatDate, useFormatters } from '@/lib/format';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemMessage } from '../../problems';
import { twoFactorStatus, userEmail, userName } from '../../user-name';

const ROLES = Object.values(UserRole);
const STATUSES = Object.values(UserStatus);

/**
 * Spec "User management by Admins": users with role, comparsas, status with the two-step state under
 * it, and last sign-in.
 */
export function UsersPage() {
  const { t, i18n } = useTranslation('identity');
  const { t: tUi } = useTranslation('ui');
  useDocumentTitle(t('users.title'));
  const [search, setSearch] = useSearchParams();
  const role = knownFilter(search.get('role'), ROLES);
  const status = knownFilter(search.get('status'), STATUSES);
  const params: ListUsersParams = {
    ...(role ? { role } : {}),
    ...(status ? { status } : {}),
  };
  const users = useListUsers(params);
  const rows = useMemo(() => (users.data?.data ?? []) as UserResponse[], [users.data]);

  // The comparsas each FiringChief runs (UI audit), from every assignment at once.
  const assignments = useListAssignments();
  const comparsasByUser = useMemo(() => {
    const byUser = new Map<string, string[]>();
    for (const { userId, comparsaName } of (assignments.data?.data ?? []) as AssignmentResponse[]) {
      byUser.set(userId, [...(byUser.get(userId) ?? []), comparsaName]);
    }
    return byUser;
  }, [assignments.data]);
  const { list } = useFormatters();
  const comparsasOf = useCallback(
    (userId: string) => list(comparsasByUser.get(userId) ?? []),
    [comparsasByUser, list],
  );

  const lastSignIn = useCallback(
    (user: UserResponse) =>
      user.lastSignInAt ? (
        formatDate(new Date(user.lastSignInAt), i18n.language, { dateStyle: 'medium', timeStyle: 'short' })
      ) : (
        <span className="text-muted-foreground">{t('users.never')}</span>
      ),
    [t, i18n.language],
  );

  const columns = useMemo<DataTableColumn<UserResponse>[]>(
    () => [
      {
        id: 'name',
        header: t('users.columns.name'),
        rowHeader: true,
        sortValue: (user) => userName(t, user),
        cell: (user) => (
          <Link to={`/users/${user.id}`} className="font-semibold text-foreground hover:underline">
            {userName(t, user)}
          </Link>
        ),
      },
      {
        id: 'email',
        header: t('users.columns.email'),
        sortValue: (user) => userEmail(user),
        // Wraps after "@" and ".", so "Last sign-in" fits at 1280 px (UI audit).
        cell: (user) => breakable(userEmail(user)),
        wrap: true,
      },
      {
        id: 'role',
        header: t('users.columns.role'),
        sortValue: (user) => tUi(`tag.role.${user.role}`),
        cell: (user) => <CategoryTag category="role" value={user.role} />,
      },
      {
        id: 'comparsas',
        header: t('users.columns.comparsas'),
        sortValue: (user) => comparsasOf(user.id),
        cell: (user) => comparsasOf(user.id),
        wrap: true,
      },
      {
        // Two-step verification as the status's second line, so "Last sign-in" fits (UI audit).
        id: 'status',
        header: t('users.columns.status'),
        sortValue: (user) => tUi(`status.user.${user.status}`),
        cell: (user) => <StatusBadge kind="user" value={user.status} />,
        secondary: (user) => (
          <span className="mt-1 flex items-center gap-1.5">
            {/* Visible short term; the full one for assistive technology. */}
            <span aria-hidden="true">{t('users.twoFactorShort')}</span>
            <span className="sr-only">{t('users.columns.twoFactor')}: </span>
            <StatusBadge kind="twoFactor" value={twoFactorStatus(user)} />
          </span>
        ),
      },
      {
        id: 'lastSignIn',
        header: t('users.columns.lastSignIn'),
        sortValue: (user) => user.lastSignInAt ?? '',
        cell: (user) => lastSignIn(user),
      },
    ],
    [t, tUi, lastSignIn, comparsasOf],
  );

  const setFilter = (key: 'role' | 'status', value: string): void => {
    setSearch(withFilter(search, key, value), { replace: true });
  };

  return (
    <>
      <PageHeader
        title={t('users.title')}
        description={t('users.description')}
        actions={
          <Button asChild>
            <Link to="/users/new">
              <UserPlus aria-hidden="true" />
              {t('users.invite')}
            </Link>
          </Button>
        }
      />
      <FilterBar
        resultText={users.isSuccess ? t('users.resultCount', { count: rows.length }) : ''}
        filters={
          <>
            <FilterSelect
              label={t('users.filters.role')}
              value={role}
              onChange={(value) => {
                setFilter('role', value);
              }}
              options={[
                { value: '', label: t('users.filters.all') },
                ...ROLES.map((value) => ({ value, label: t(`roles.${value}`) })),
              ]}
            />
            <FilterSelect
              label={t('users.filters.status')}
              value={status}
              onChange={(value) => {
                setFilter('status', value);
              }}
              options={[
                { value: '', label: t('users.filters.all') },
                ...STATUSES.map((value) => ({ value, label: tUi(`status.user.${value}`) })),
              ]}
            />
          </>
        }
      />
      {/* Without them the column would look like "no comparsas": say so instead. */}
      {assignments.isError && (
        <LoadFailure
          error={assignments.error}
          consequence={t('users.comparsasFailed')}
          onRetry={() => assignments.refetch()}
        />
      )}
      {users.isError ? (
        <AlertBanner severity="error">{problemMessage(t, users.error)}</AlertBanner>
      ) : (
        <DataTable
          caption={t('users.caption')}
          data={rows}
          columns={columns}
          getRowId={(user) => user.id}
          getRowHref={(user) => `/users/${user.id}`}
          mobileRow={(user) => (
            <>
              <Link to={`/users/${user.id}`} className="font-semibold text-foreground">
                {userName(t, user)}
              </Link>
              <span className="text-help break-all text-muted-foreground">{userEmail(user)}</span>
              {/* Every column of the table, each value with its term for screen readers (WCAG 1.3.1). */}
              <span className="flex flex-wrap items-center gap-2 text-help text-muted-foreground">
                <span>
                  <span className="sr-only">{t('users.columns.role')}: </span>
                  <CategoryTag category="role" value={user.role} />
                </span>
                {comparsasOf(user.id) && (
                  <span>
                    <span className="sr-only">{t('users.columns.comparsas')}: </span>
                    {comparsasOf(user.id)}
                  </span>
                )}
                <span>
                  <span className="sr-only">{t('users.columns.status')}: </span>
                  <StatusBadge kind="user" value={user.status} />
                </span>
                <span className="flex items-center gap-1.5">
                  {t('users.columns.twoFactor')}
                  <StatusBadge kind="twoFactor" value={twoFactorStatus(user)} />
                </span>
              </span>
              <span className="text-help text-muted-foreground">
                {t('users.columns.lastSignIn')}: {lastSignIn(user)}
              </span>
            </>
          )}
          isLoading={users.isPending}
        />
      )}
    </>
  );
}
