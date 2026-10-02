import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { z } from 'zod';
import { getListUsersQueryKey, useInviteUser } from '@/api/generated/users/users';
import type { UserResponse } from '@/api/generated/model';
import { ApiProblemError, responseData } from '@/api/http';
import { ActionBar } from '@/components/app/ActionBar';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { FormLayout } from '@/components/app/FormLayout';
import { PageHeader } from '@/components/app/PageHeader';
import { TextInput } from '@/components/app/TextInput';
import { useAppForm } from '@/components/app/use-app-form';
import { DEFAULT_LANGUAGE, matchLanguage } from '@/i18n/config';
import { noticeState } from '@/lib/notices';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { emailField, problemCode, problemMessage } from '../../problems';
import { UserFields } from './UserFields';
import { userFieldsSchema } from './userFieldsSchema';

const schema = userFieldsSchema.extend({ email: emailField });

/** The user an invitation created although its email could not be sent, if that is what happened. */
function createdUserId(error: unknown): string | undefined {
  const userId = error instanceof ApiProblemError ? error.problem?.userId : undefined;
  return problemCode(error) === 'email.sendFailed' && typeof userId === 'string' ? userId : undefined;
}
type Values = z.infer<typeof schema>;

/** Spec "Invitation-only accounts": an Admin invites a user by email (form template). */
export function InviteUserPage() {
  const { t, i18n } = useTranslation('identity');
  useDocumentTitle(t('users.inviteTitle'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const invite = useInviteUser();
  const form = useAppForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: '',
      name: '',
      role: 'FIRING_CHIEF',
      locale: matchLanguage(i18n.resolvedLanguage ?? '') ?? DEFAULT_LANGUAGE,
    },
  });

  const onSubmit = async (values: Values): Promise<void> => {
    let created: UserResponse;
    try {
      created = responseData(await invite.mutateAsync({ data: values }));
    } catch (error) {
      await queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() });
      const userId = createdUserId(error);
      if (userId) {
        // The user was created: continue on their page, where the invitation can be resent.
        await navigate(`/users/${userId}`, { state: noticeState(t('errors.email.sendFailed'), 'error') });
        return;
      }
      // A taken email is shown on its field; anything else in the error summary.
      const field = problemCode(error) === 'users.emailTaken' ? 'email' : 'root.server';
      form.setError(field, { type: 'server', message: problemMessage(t, error) });
      return;
    }
    await queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() });
    await navigate(`/users/${created.id}`, {
      state: noticeState(t('users.invited', { email: values.email })),
    });
  };

  const fields = (
    <>
      <FormField control={form.control} name="email" label={t('users.fields.email')} width="long">
        {(field) => (
          <TextInput type="email" autoComplete="off" inputMode="email" spellCheck={false} {...field} />
        )}
      </FormField>
      <UserFields control={form.control} />
    </>
  );

  return (
    <>
      <PageHeader
        title={t('users.inviteTitle')}
        description={t('users.inviteDescription')}
        back={{ to: '/users', label: t('users.detailBack') }}
      />
      <Form form={form} onSubmit={onSubmit}>
        <FormLayout
          sections={[{ id: 'account', title: t('users.sections.account'), content: fields }]}
          actions={
            <ActionBar
              secondary={
                <Button asChild variant="secondary">
                  <Link to="/users">{t('users.cancel')}</Link>
                </Button>
              }
              primary={
                <Button type="submit" pending={invite.isPending || form.formState.isSubmitting}>
                  {t('users.sendInvitation')}
                </Button>
              }
            />
          }
        />
      </Form>
    </>
  );
}
