import { UserPlus } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useListUsers } from '@/api/generated/users/users';
import { UserRole, UserStatus, type ListUsersParams, type UserResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { FilterBar } from '@/components/app/FilterBar';
import { FilterSelect } from '@/components/app/FilterSelect';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { formatDate } from '@/lib/format';
import { knownFilter, withFilter } from '@/lib/search-filters';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemMessage } from '../../problems';

const ROLES = Object.values(UserRole);
const STATUSES = Object.values(UserStatus);

/** Spec "User management by Admins": users with role, status, two-step state and last sign-in. */
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

  const columns = useMemo<DataTableColumn<UserResponse>[]>(
    () => [
      {
        id: 'name',
        header: t('users.columns.name'),
        sortValue: (user) => user.name,
        cell: (user) => (
          <Link to={`/users/${user.id}`} className="font-semibold text-foreground hover:underline">
            {user.name}
          </Link>
        ),
      },
      {
        id: 'email',
        header: t('users.columns.email'),
        sortValue: (user) => user.email,
        cell: (user) => user.email,
      },
      { id: 'role', header: t('users.columns.role'), cell: (user) => t(`roles.${user.role}`) },
      {
        id: 'status',
        header: t('users.columns.status'),
        cell: (user) => <StatusBadge kind="user" value={user.status} />,
      },
      {
        id: 'twoFactor',
        header: t('users.columns.twoFactor'),
        cell: (user) => (user.twoFactorEnabled ? t('users.enabled') : t('users.disabled')),
      },
      {
        id: 'lastSignIn',
        header: t('users.columns.lastSignIn'),
        sortValue: (user) => user.lastSignInAt ?? '',
        cell: (user) =>
          user.lastSignInAt
            ? formatDate(new Date(user.lastSignInAt), i18n.language, {
                dateStyle: 'medium',
                timeStyle: 'short',
              })
            : t('users.never'),
      },
    ],
    [t, i18n.language],
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
                {user.name}
              </Link>
              <span className="text-help break-all text-muted-foreground">{user.email}</span>
              <span className="flex flex-wrap items-center gap-2 text-help text-muted-foreground">
                {t(`roles.${user.role}`)}
                <StatusBadge kind="user" value={user.status} />
              </span>
            </>
          )}
          isLoading={users.isPending}
        />
      )}
    </>
  );
}
