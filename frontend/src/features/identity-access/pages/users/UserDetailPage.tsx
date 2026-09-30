import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router';
import {
  getGetUserQueryKey,
  getListUsersQueryKey,
  useDeactivateUser,
  useGetUser,
  useReactivateUser,
  useResendInvitation,
  useResetTwoFactor,
  useUpdateUser,
} from '@/api/generated/users/users';
import type { UserResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { Form } from '@/components/app/FormField';
import { FormSection } from '@/components/app/FormSection';
import { PageHeader } from '@/components/app/PageHeader';
import { StatusBadge } from '@/components/app/StatusBadge';
import { DEFAULT_LANGUAGE, matchLanguage } from '@/i18n/config';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemMessage } from '../../problems';
import { SESSION_QUERY_KEY, useSession } from '../../session';
import { UserFields } from './UserFields';
import { userFieldsSchema, type UserFieldValues } from './userFieldsSchema';

interface Notice {
  /** A new id per notice, so each one is shown (and focused) afresh. */
  id: number;
  severity: 'success' | 'error';
  text: string;
}

type Announce = (severity: Notice['severity'], text: string) => void;

/** Refreshes what an Admin's change to `userId` affects, including their own session. */
function useRefreshAfterChange(userId: string): () => Promise<void> {
  const queryClient = useQueryClient();
  const session = useSession();
  const isSelf = session.account?.id === userId;

  return async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetUserQueryKey(userId) }),
      queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() }),
      // An Admin who changed their own role must not keep the old one in the UI.
      isSelf ? queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY }) : undefined,
    ]);
  };
}

function EditForm({ user, announce }: { user: UserResponse; announce: Announce }) {
  const { t } = useTranslation('identity');
  const update = useUpdateUser();
  const refresh = useRefreshAfterChange(user.id);
  const values = useMemo<UserFieldValues>(
    () => ({ name: user.name, role: user.role, locale: matchLanguage(user.locale) ?? DEFAULT_LANGUAGE }),
    [user.name, user.role, user.locale],
  );
  // Follows the server's values, but a refresh never discards what the Admin is typing.
  const form = useForm<UserFieldValues>({
    resolver: zodResolver(userFieldsSchema),
    values,
    resetOptions: { keepDirtyValues: true },
  });

  const onSubmit = async (submitted: UserFieldValues): Promise<void> => {
    try {
      await update.mutateAsync({ id: user.id, data: submitted });
    } catch (error) {
      announce('error', problemMessage(t, error));
      return;
    }
    form.reset(submitted);
    announce('success', t('users.saved'));
    await refresh();
  };

  return (
    <Form form={form} onSubmit={onSubmit} className="max-w-xl">
      <UserFields control={form.control} />
      <Button type="submit" pending={update.isPending}>
        {t('users.save')}
      </Button>
    </Form>
  );
}

function Actions({ user, announce }: { user: UserResponse; announce: Announce }) {
  const { t } = useTranslation('identity');
  const deactivate = useDeactivateUser();
  const reactivate = useReactivateUser();
  const resend = useResendInvitation();
  const resetTwoFactor = useResetTwoFactor();
  const refresh = useRefreshAfterChange(user.id);

  /** A direct action: the page announces the outcome, success or failure. */
  const act = async (action: () => Promise<unknown>, success: string): Promise<void> => {
    try {
      await action();
    } catch (error) {
      announce('error', problemMessage(t, error));
      return;
    }
    announce('success', success);
    await refresh();
  };

  /** A confirmed action: a failure stays in the dialog, with its reason; success is announced. */
  const confirmed = async (action: () => Promise<unknown>, success: string): Promise<void> => {
    try {
      await action();
    } catch (error) {
      throw new ConfirmFailure(problemMessage(t, error));
    }
    announce('success', success);
    await refresh();
  };

  return (
    <FormSection title={t('users.actionsTitle')}>
      <div className="flex flex-wrap gap-3">
        {user.status === 'INVITED' && (
          <Button
            type="button"
            variant="secondary"
            pending={resend.isPending}
            onClick={() => void act(() => resend.mutateAsync({ id: user.id }), t('users.resent'))}
          >
            {t('users.resendInvitation')}
          </Button>
        )}
        {user.status === 'DEACTIVATED' ? (
          <Button
            type="button"
            variant="secondary"
            pending={reactivate.isPending}
            onClick={() => void act(() => reactivate.mutateAsync({ id: user.id }), t('users.reactivated'))}
          >
            {t('users.reactivate')}
          </Button>
        ) : (
          <ConfirmDialog
            title={t('users.deactivateTitle', { name: user.name })}
            description={t('users.deactivateDescription')}
            confirmLabel={t('users.deactivate')}
            onConfirm={() => confirmed(() => deactivate.mutateAsync({ id: user.id }), t('users.deactivated'))}
            trigger={
              <Button type="button" variant="destructive">
                {t('users.deactivate')}
              </Button>
            }
          />
        )}
        {user.twoFactorEnabled && (
          <ConfirmDialog
            title={t('users.resetTwoFactorTitle', { name: user.name })}
            description={t('users.resetTwoFactorDescription')}
            confirmLabel={t('users.resetTwoFactor')}
            onConfirm={() =>
              confirmed(() => resetTwoFactor.mutateAsync({ id: user.id }), t('users.twoFactorReset'))
            }
            trigger={
              <Button type="button" variant="destructive">
                {t('users.resetTwoFactor')}
              </Button>
            }
          />
        )}
      </div>
    </FormSection>
  );
}

/** A notice handed over by the page that navigated here (e.g. "invitation sent"). */
function noticeFromState(state: unknown): Notice | undefined {
  const { notice, error } = (state ?? {}) as { notice?: unknown; error?: unknown };
  if (typeof notice === 'string') {
    return { id: 0, severity: 'success', text: notice };
  }
  return typeof error === 'string' ? { id: 0, severity: 'error', text: error } : undefined;
}

function UserDetail({ id }: { id: string }) {
  const { t } = useTranslation('identity');
  const location = useLocation();
  const navigate = useNavigate();
  const user = useGetUser(id, { query: { retry: false } });
  const details = user.data?.data as UserResponse | undefined;
  useDocumentTitle(details?.name ?? t('users.title'));
  const [notice, setNotice] = useState(() => noticeFromState(location.state));
  const announce: Announce = (severity, text) => {
    setNotice((previous) => ({ id: (previous?.id ?? 0) + 1, severity, text }));
  };

  // The handed-over notice is shown once: not again after a reload or on coming back.
  useEffect(() => {
    if (location.state !== null) {
      void navigate('.', { replace: true, state: null });
    }
  }, [location.state, navigate]);

  if (user.isError) {
    return (
      <>
        <PageHeader title={t('users.title')} back={{ to: '/users', label: t('users.detailBack') }} />
        <AlertBanner severity="error">{problemMessage(t, user.error)}</AlertBanner>
      </>
    );
  }
  if (!details) {
    return <PageHeader title={t('users.title')} back={{ to: '/users', label: t('users.detailBack') }} />;
  }

  return (
    <>
      <PageHeader
        title={details.name}
        description={`${details.email} · ${t(`roles.${details.role}`)}`}
        back={{ to: '/users', label: t('users.detailBack') }}
        actions={<StatusBadge kind="user" value={details.status} />}
      />
      <div className="grid gap-10">
        {notice && (
          <AlertBanner key={notice.id} severity={notice.severity} className="max-w-xl" focusOnMount>
            {notice.text}
          </AlertBanner>
        )}
        <EditForm user={details} announce={announce} />
        <Actions user={details} announce={announce} />
      </div>
    </>
  );
}

/** Spec "User management by Admins": edit a user and act on their account. */
export function UserDetailPage() {
  const { id = '' } = useParams();
  return <UserDetail key={id} id={id} />;
}
