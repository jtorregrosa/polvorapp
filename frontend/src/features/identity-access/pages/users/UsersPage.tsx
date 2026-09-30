import { UserPlus } from 'lucide-react';
import { useId, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { useListUsers } from '@/api/generated/users/users';
import { UserRole, UserStatus, type ListUsersParams, type UserResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { DataTable, type DataTableColumn } from '@/components/app/DataTable';
import { PageHeader } from '@/components/app/PageHeader';
import { SelectInput } from '@/components/app/SelectInput';
import { StatusBadge } from '@/components/app/StatusBadge';
import { formatDate } from '@/lib/format';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemMessage } from '../../problems';

const ROLES = Object.values(UserRole);
const STATUSES = Object.values(UserStatus);

/** A filter from the address, if it is one the API knows; otherwise no filter. */
function known<T extends string>(value: string | null, allowed: readonly T[]): T | '' {
  return allowed.find((candidate) => candidate === value) ?? '';
}

function Filter({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: { value: string; label: string }[];
  onChange: (value: string) => void;
}) {
  const id = useId();
  return (
    <div className="grid gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      <SelectInput
        id={id}
        value={value}
        options={options}
        onChange={(event) => {
          onChange(event.target.value);
        }}
      />
    </div>
  );
}

/** Spec "User management by Admins": users with role, status, two-step state and last sign-in. */
export function UsersPage() {
  const { t, i18n } = useTranslation('identity');
  const { t: tUi } = useTranslation('ui');
  useDocumentTitle(t('users.title'));
  const [search, setSearch] = useSearchParams();
  const role = known(search.get('role'), ROLES);
  const status = known(search.get('status'), STATUSES);
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
          <Link
            to={`/users/${user.id}`}
            className="font-medium text-primary underline-offset-4 hover:underline"
          >
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
    const next = new URLSearchParams(search);
    if (value) {
      next.set(key, value);
    } else {
      next.delete(key);
    }
    setSearch(next, { replace: true });
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
      <div className="mb-4 flex flex-wrap gap-4">
        <Filter
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
        <Filter
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
      </div>
      {users.isError ? (
        <AlertBanner severity="error">{problemMessage(t, users.error)}</AlertBanner>
      ) : (
        <DataTable
          caption={t('users.caption')}
          data={rows}
          columns={columns}
          getRowId={(user) => user.id}
          isLoading={users.isPending}
        />
      )}
    </>
  );
}
