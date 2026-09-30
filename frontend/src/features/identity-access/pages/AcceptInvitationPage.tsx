import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router';
import { useAcceptInvitation, useValidateInvitation } from '@/api/generated/auth/auth';
import type { InvitationResponse } from '@/api/generated/model';
import { AlertBanner } from '@/components/app/AlertBanner';
import { PageHeader } from '@/components/app/PageHeader';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemCode, problemMessage } from '../problems';
import { NewPasswordForm } from './NewPasswordForm';

/** Spec "Invitation-only accounts": the invitee sets a password, then enrols an authenticator. */
export function AcceptInvitationPage() {
  const { t } = useTranslation('identity');
  const [search] = useSearchParams();
  const user = search.get('user') ?? '';
  const token = search.get('token') ?? '';
  const completeLink = user !== '' && token !== '';
  const navigate = useNavigate();
  const invitation = useValidateInvitation(
    { user, token },
    { query: { retry: false, staleTime: Infinity, enabled: completeLink } },
  );
  const accept = useAcceptInvitation();
  const invalidLink =
    !completeLink ||
    problemCode(invitation.error) === 'auth.invalidLink' ||
    problemCode(accept.error) === 'auth.invalidLink';
  useDocumentTitle(t(invalidLink ? 'invitation.invalidTitle' : 'invitation.title'));

  if (invalidLink) {
    return (
      <PageHeader
        title={t('invitation.invalidTitle')}
        description={t('invitation.invalidDescription')}
        focusOnMount={accept.isError}
      />
    );
  }

  if (invitation.isPending) {
    return <PageHeader title={t('invitation.title')} />;
  }

  if (invitation.isError) {
    return (
      <>
        <PageHeader title={t('invitation.title')} />
        <AlertBanner severity="error">{problemMessage(t, invitation.error)}</AlertBanner>
      </>
    );
  }

  const invitee = invitation.data.data as InvitationResponse;
  const onSubmit = async (password: string): Promise<void> => {
    try {
      await accept.mutateAsync({ data: { userId: user, token, password } });
      await navigate('/enrolment', { replace: true });
    } catch {
      // The form shows the error.
    }
  };

  return (
    <>
      <PageHeader
        title={t('invitation.title')}
        description={t('invitation.description', { name: invitee.name, email: invitee.email })}
      />
      <NewPasswordForm
        passwordLabel={t('invitation.password')}
        confirmLabel={t('invitation.confirmPassword')}
        submitLabel={t('invitation.submit')}
        onSubmit={onSubmit}
        isPending={accept.isPending}
        error={accept.error}
      />
    </>
  );
}
