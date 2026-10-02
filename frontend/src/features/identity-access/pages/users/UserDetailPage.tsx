import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { CirclePlay, KeyRound, Send, UserX } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
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
import { getAccount } from '@/api/generated/account/account';
import type { UserResponse } from '@/api/generated/model';
import { AlertBanner, NoticeBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { explainFailure } from '@/components/app/confirm-failure';
import { DescriptionList } from '@/components/app/DescriptionList';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { PageHeader } from '@/components/app/PageHeader';
import { RecordHeader, type MoreAction } from '@/components/app/RecordHeader';
import { SectionCard } from '@/components/app/SectionCard';
import { SectionGrid } from '@/components/app/SectionGrid';
import { useSaveNotice } from '@/components/app/save-notice';
import { StatusBadge } from '@/components/app/StatusBadge';
import { useAppForm } from '@/components/app/use-app-form';
import { UserComparsasSection } from '@/features/federation-catalog/components/UserComparsasSection';
import { DEFAULT_LANGUAGE, matchLanguage } from '@/i18n/config';
import { formatDate } from '@/lib/format';
import { useNotice, type Announce, type Notice } from '@/lib/notices';
import { useInvalidate } from '@/lib/use-invalidate';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemMessage } from '../../problems';
import { SESSION_QUERY_KEY, useForgetSession, useSession } from '../../session';
import { UserFields } from './UserFields';
import { userFieldsSchema, type UserFieldValues } from './userFieldsSchema';

/**
 * Refreshes what an Admin's change to `userId` affects. A change to their own account reloads
 * their session and everything cached with the old role; if the session cannot be reloaded they
 * are signed out rather than left with privileges they may no longer have.
 */
function useRefreshAfterChange(userId: string): () => Promise<void> {
  const queryClient = useQueryClient();
  const session = useSession();
  const forgetSession = useForgetSession();
  const isSelf = session.account?.id === userId;
  const refreshUser = useInvalidate([getGetUserQueryKey(userId), getListUsersQueryKey()]);

  return async () => {
    if (!isSelf) {
      await refreshUser();
      return;
    }
    try {
      await queryClient.query({
        queryKey: SESSION_QUERY_KEY,
        queryFn: async () => (await getAccount()).data,
        staleTime: 0, // The role just changed: never the cached account.
      });
    } catch {
      await forgetSession();
      return;
    }
    await queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] !== SESSION_QUERY_KEY[0] });
  };
}

/** Name, role and email language, read-only, with their edit panel (spec: Detail pages in read mode). */
function AccountSection({ user }: { user: UserResponse }) {
  const { t, i18n } = useTranslation('identity');
  const { t: tCommon } = useTranslation();
  const update = useUpdateUser();
  const refresh = useRefreshAfterChange(user.id);
  const values = useMemo<UserFieldValues>(
    () => ({ name: user.name, role: user.role, locale: matchLanguage(user.locale) ?? DEFAULT_LANGUAGE }),
    [user.name, user.role, user.locale],
  );
  const form = useAppForm<UserFieldValues>({
    resolver: zodResolver(userFieldsSchema),
    defaultValues: values,
  });

  // The user API has no version: the last save wins (design D9).
  const save = async (submitted: UserFieldValues): Promise<EditResult> => {
    try {
      await update.mutateAsync({ id: user.id, data: submitted });
    } catch (error) {
      // E.g. the last active Admin cannot stop being one: said in the panel's error summary.
      return { status: 'rejected', reason: problemMessage(t, error) };
    }
    await refresh();
    return { status: 'saved' };
  };

  return (
    <SectionCard
      title={t('users.sections.account')}
      action={
        <EditSheet
          title={t('users.sections.edit')}
          sectionName={t('users.sections.editName')}
          form={form}
          values={values}
          onSave={save}
        >
          <UserFields control={form.control} />
        </EditSheet>
      }
    >
      <DescriptionList
        items={[
          { term: t('users.fields.name'), value: user.name },
          { term: t('users.fields.email'), value: <span className="break-all">{user.email}</span> },
          { term: t('users.fields.role'), value: t(`roles.${user.role}`) },
          {
            term: t('users.fields.locale'),
            // Each language is named in itself (WCAG 3.1.2).
            value: <span lang={values.locale}>{tCommon(`shell.language.options.${values.locale}`)}</span>,
          },
          {
            term: t('users.columns.twoFactor'),
            value: user.twoFactorEnabled ? t('users.enabled') : t('users.disabled'),
          },
          {
            term: t('users.columns.lastSignIn'),
            value: user.lastSignInAt
              ? formatDate(new Date(user.lastSignInAt), i18n.language, {
                  dateStyle: 'medium',
                  timeStyle: 'short',
                })
              : t('users.never'),
          },
        ]}
      />
    </SectionCard>
  );
}

/**
 * Resending an invitation is the header's action; resetting two-step verification, reactivating
 * and deactivating are in "More actions", the last one set apart (spec: Action hierarchy).
 */
function useUserActions(user: UserResponse, announce: Announce, clearNotice: () => void) {
  const { t } = useTranslation('identity');
  const notify = useSaveNotice();
  const deactivate = useDeactivateUser();
  const reactivate = useReactivateUser();
  const resend = useResendInvitation();
  const resetTwoFactor = useResetTwoFactor();
  const refresh = useRefreshAfterChange(user.id);
  const moreActions = useRef<HTMLButtonElement>(null);
  const [dialog, setDialog] = useState<'deactivate' | 'resetTwoFactor'>();

  /** A direct action: a failure is announced with focus, a success politely. */
  const act = async (action: () => Promise<unknown>, success: string): Promise<void> => {
    clearNotice(); // A notice of an earlier action no longer applies.
    try {
      await action();
    } catch (error) {
      announce('error', problemMessage(t, error));
      return;
    }
    notify(success);
    await refresh();
  };

  /** A confirmed action: a failure stays in the dialog, with its reason. */
  const confirmed = (action: () => Promise<unknown>) =>
    explainFailure(action, (error) => problemMessage(t, error));

  const afterConfirmed = (success: string) => () => {
    clearNotice();
    moreActions.current?.focus();
    notify(success);
    void refresh();
  };

  const items: MoreAction[] = [];
  if (user.twoFactorEnabled) {
    items.push({
      id: 'resetTwoFactor',
      label: t('users.resetTwoFactor'),
      icon: KeyRound,
      onSelect: () => {
        setDialog('resetTwoFactor');
      },
    });
  }
  items.push(
    user.status === 'DEACTIVATED'
      ? {
          id: 'reactivate',
          label: t('users.reactivate'),
          icon: CirclePlay,
          // One request at a time.
          disabled: reactivate.isPending,
          onSelect: () => {
            void act(() => reactivate.mutateAsync({ id: user.id }), t('users.reactivated'));
          },
        }
      : {
          id: 'deactivate',
          label: t('users.deactivate'),
          icon: UserX,
          destructive: true,
          onSelect: () => {
            setDialog('deactivate');
          },
        },
  );

  const close = (open: boolean) => {
    if (!open) setDialog(undefined);
  };

  const headerAction = user.status === 'INVITED' && (
    <Button
      variant="secondary"
      icon={Send}
      pending={resend.isPending}
      onClick={() => void act(() => resend.mutateAsync({ id: user.id }), t('users.resent'))}
    >
      {t('users.resendInvitation')}
    </Button>
  );

  const dialogs = (
    <>
      <ConfirmDialog
        open={dialog === 'deactivate'}
        onOpenChange={close}
        returnFocus={moreActions}
        title={t('users.deactivateTitle', { name: user.name })}
        description={t('users.deactivateDescription')}
        confirmLabel={t('users.deactivate')}
        onConfirm={() => confirmed(() => deactivate.mutateAsync({ id: user.id }))}
        onConfirmed={afterConfirmed(t('users.deactivated'))}
      />
      <ConfirmDialog
        open={dialog === 'resetTwoFactor'}
        onOpenChange={close}
        returnFocus={moreActions}
        tone="primary"
        title={t('users.resetTwoFactorTitle', { name: user.name })}
        description={t('users.resetTwoFactorDescription')}
        confirmLabel={t('users.resetTwoFactor')}
        onConfirm={() => confirmed(() => resetTwoFactor.mutateAsync({ id: user.id }))}
        onConfirmed={afterConfirmed(t('users.twoFactorReset'))}
      />
    </>
  );

  return { items, headerAction, dialogs, moreActions };
}

function UserRecord({
  user,
  announce,
  notice,
  clearNotice,
}: {
  user: UserResponse;
  announce: Announce;
  notice?: Notice;
  clearNotice: () => void;
}) {
  const { t } = useTranslation('identity');
  const actions = useUserActions(user, announce, clearNotice);
  return (
    <>
      <RecordHeader
        back={{ to: '/users', label: t('users.detailBack') }}
        context={t(`roles.${user.role}`)}
        name={user.name}
        statuses={
          <>
            <StatusBadge kind="user" value={user.status} />
            <span className="text-help text-muted-foreground">
              {t('users.twoFactorState', {
                state: user.twoFactorEnabled ? t('users.enabled') : t('users.disabled'),
              })}
            </span>
          </>
        }
        actions={actions.headerAction || undefined}
        moreActions={actions.items}
        moreActionsRef={actions.moreActions}
      />
      <NoticeBanner notice={notice} />
      <SectionGrid>
        <AccountSection user={user} />
        <UserComparsasSection user={user} />
      </SectionGrid>
      {actions.dialogs}
    </>
  );
}

function UserDetail({ id }: { id: string }) {
  const { t } = useTranslation('identity');
  const user = useGetUser(id, { query: { retry: false } });
  const details = user.data?.data as UserResponse | undefined;
  useDocumentTitle(details?.name ?? t('users.title'));
  // E.g. "invitation sent", handed over by the invitation page; shown once.
  const [notice, announce, clearNotice] = useNotice();
  const back = { to: '/users', label: t('users.detailBack') };

  if (user.isError && !details) {
    return (
      <>
        <PageHeader title={t('users.title')} back={back} />
        <AlertBanner severity="error">{problemMessage(t, user.error)}</AlertBanner>
      </>
    );
  }
  if (!details) {
    return <PageHeader title={t('users.title')} back={back} />;
  }
  return <UserRecord user={details} announce={announce} notice={notice} clearNotice={clearNotice} />;
}

/** Spec "User management by Admins": one user in read mode; edit them and act on their account. */
export function UserDetailPage() {
  const { id = '' } = useParams();
  return <UserDetail key={id} id={id} />;
}
