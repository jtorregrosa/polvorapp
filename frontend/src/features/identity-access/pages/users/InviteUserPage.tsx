import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { z } from 'zod';
import { getListUsersQueryKey, useInviteUser } from '@/api/generated/users/users';
import type { UserResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { TextInput } from '@/components/app/TextInput';
import { DEFAULT_LANGUAGE, matchLanguage } from '@/i18n/config';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { ApiProblemError, responseData } from '@/api/http';
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

/** Spec "Invitation-only accounts": an Admin invites a user by email. */
export function InviteUserPage() {
  const { t, i18n } = useTranslation('identity');
  useDocumentTitle(t('users.inviteTitle'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const invite = useInviteUser();
  const form = useForm<Values>({
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
        await navigate(`/users/${userId}`, { state: { error: t('errors.email.sendFailed') } });
      }
      return;
    }
    await queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() });
    await navigate(`/users/${created.id}`, {
      state: { notice: t('users.invited', { email: values.email }) },
    });
  };

  return (
    <>
      <PageHeader
        title={t('users.inviteTitle')}
        description={t('users.inviteDescription')}
        back={{ to: '/users', label: t('users.detailBack') }}
      />
      <Form form={form} onSubmit={onSubmit} className="max-w-xl">
        <FormField control={form.control} name="email" label={t('users.fields.email')} required>
          {(field) => <TextInput type="email" autoComplete="off" inputMode="email" {...field} />}
        </FormField>
        <UserFields control={form.control} />
        {invite.isError && !createdUserId(invite.error) && (
          <AlertBanner severity="error">{problemMessage(t, invite.error)}</AlertBanner>
        )}
        <Button type="submit" pending={invite.isPending}>
          {t('users.sendInvitation')}
        </Button>
      </Form>
    </>
  );
}
