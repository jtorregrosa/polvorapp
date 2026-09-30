import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router';
import { z } from 'zod';
import { useForgotPassword, useResetPassword } from '@/api/generated/auth/auth';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { TextInput } from '@/components/app/TextInput';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { emailField, problemCode, problemMessage } from '../problems';
import { NewPasswordForm } from './NewPasswordForm';

function BackToSignIn() {
  const { t } = useTranslation('identity');
  return (
    <Link to="/login" className="font-medium text-primary underline-offset-4 hover:underline">
      {t('password.backToSignIn')}
    </Link>
  );
}

const forgotSchema = z.object({ email: emailField });
type ForgotValues = z.infer<typeof forgotSchema>;

/** Spec "Password reset by email": the same confirmation whether or not the account exists. */
export function ForgotPasswordPage() {
  const { t } = useTranslation('identity');
  const forgot = useForgotPassword();
  useDocumentTitle(t(forgot.isSuccess ? 'password.sentTitle' : 'password.forgotTitle'));
  const form = useForm<ForgotValues>({ resolver: zodResolver(forgotSchema), defaultValues: { email: '' } });

  if (forgot.isSuccess) {
    return (
      <>
        <PageHeader
          title={t('password.sentTitle')}
          description={t('password.sentDescription')}
          focusOnMount
        />
        <p className="text-sm">
          <BackToSignIn />
        </p>
      </>
    );
  }

  return (
    <>
      <PageHeader title={t('password.forgotTitle')} description={t('password.forgotDescription')} />
      <Form
        form={form}
        onSubmit={async (values) => {
          await forgot.mutateAsync({ data: values }).catch(() => undefined);
        }}
        requiredNote={false}
      >
        <FormField control={form.control} name="email" label={t('signIn.email')} required>
          {(field) => <TextInput type="email" autoComplete="username" inputMode="email" {...field} />}
        </FormField>
        {forgot.isError && <AlertBanner severity="error">{problemMessage(t, forgot.error)}</AlertBanner>}
        <Button type="submit" pending={forgot.isPending}>
          {t('password.forgotSubmit')}
        </Button>
      </Form>
      <p className="mt-6 text-sm">
        <BackToSignIn />
      </p>
    </>
  );
}

/** Spec "Password reset by email": a new password from the emailed link. */
export function ResetPasswordPage() {
  const { t } = useTranslation('identity');
  const [search] = useSearchParams();
  const reset = useResetPassword();
  const invalidLink =
    problemCode(reset.error) === 'auth.invalidLink' || !search.get('user') || !search.get('token');
  useDocumentTitle(
    t(reset.isSuccess ? 'password.resetDone' : invalidLink ? 'password.invalidTitle' : 'password.resetTitle'),
  );

  if (reset.isSuccess) {
    return (
      <>
        <PageHeader title={t('password.resetDone')} focusOnMount />
        <p className="text-sm">
          <BackToSignIn />
        </p>
      </>
    );
  }
  if (invalidLink) {
    return (
      <>
        <PageHeader
          title={t('password.invalidTitle')}
          description={t('password.invalidDescription')}
          focusOnMount={reset.isError}
        />
        <p className="text-sm">
          <Link to="/password/forgot" className="font-medium text-primary underline-offset-4 hover:underline">
            {t('password.forgotTitle')}
          </Link>
        </p>
      </>
    );
  }

  const onSubmit = async (password: string): Promise<void> => {
    await reset
      .mutateAsync({ data: { userId: search.get('user') ?? '', token: search.get('token') ?? '', password } })
      .catch(() => undefined);
  };

  return (
    <>
      <PageHeader title={t('password.resetTitle')} description={t('password.resetDescription')} />
      <NewPasswordForm
        passwordLabel={t('account.newPassword')}
        confirmLabel={t('account.confirmPassword')}
        submitLabel={t('password.resetSubmit')}
        onSubmit={onSubmit}
        isPending={reset.isPending}
        error={reset.error}
      />
    </>
  );
}
