import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useAppForm } from '@/components/app/use-app-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { useLogin } from '@/api/generated/auth/auth';
import type { SignInStep } from '@/api/generated/model';
import { responseData } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { PasswordInput } from '@/components/app/PasswordInput';
import { TextInput } from '@/components/app/TextInput';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { emailField, messages, problemMessage } from '../problems';
import { safeReturnTo, useCompleteSignIn } from '../session';

const schema = z.object({ email: emailField, password: z.string().min(1, messages.required) });
type Values = z.infer<typeof schema>;

/** Where the next step lives, keeping the page to return to; `undefined` once signed in. */
function nextStepPath(next: SignInStep, returnTo: string): string | undefined {
  const query = `?returnTo=${encodeURIComponent(returnTo)}`;
  switch (next) {
    case 'SECOND_FACTOR':
      return `/login/second-factor${query}`;
    case 'ENROL':
      return `/enrolment${query}`;
    case 'DONE':
      return undefined;
    default: {
      const unknownStep: never = next;
      throw new Error(`Unknown sign-in step: ${String(unknownStep)}`);
    }
  }
}

/** Why the sign-in page is shown again, if the address says so. */
function ReasonNotice({ reason }: { reason: string | null }) {
  const { t } = useTranslation('identity');
  if (reason === 'expired') {
    return (
      <AlertBanner severity="info" focusOnMount>
        {t('signIn.expired')}
      </AlertBanner>
    );
  }
  if (reason === 'stepExpired') {
    return (
      <AlertBanner severity="info" focusOnMount>
        {t('errors.auth.stepExpired')}
      </AlertBanner>
    );
  }
  return null;
}

/** Spec "Sign-in with two-factor authentication": the password step. */
export function SignInPage() {
  const { t } = useTranslation('identity');
  useDocumentTitle(t('signIn.title'));
  const [search] = useSearchParams();
  const returnTo = safeReturnTo(search.get('returnTo'));
  const navigate = useNavigate();
  const completeSignIn = useCompleteSignIn();
  const login = useLogin();
  const [error, setError] = useState<unknown>();
  const form = useAppForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '' },
  });

  const onSubmit = async (values: Values): Promise<void> => {
    setError(undefined);
    let next: SignInStep;
    try {
      next = responseData(await login.mutateAsync({ data: values })).next;
    } catch (failure) {
      setError(failure);
      form.setValue('password', '');
      form.setFocus('password');
      return;
    }
    const path = nextStepPath(next, returnTo);
    if (path) {
      await navigate(path);
      return;
    }
    try {
      await completeSignIn();
    } catch (failure) {
      setError(failure); // Signed in, but the session could not be loaded: submitting again retries.
      return;
    }
    await navigate(returnTo, { replace: true });
  };

  return (
    <>
      <PageHeader title={t('signIn.title')} description={t('signIn.description')} />
      {error === undefined && <ReasonNotice reason={search.get('reason')} />}
      <Form form={form} onSubmit={onSubmit} requiredNote={false}>
        <FormField control={form.control} name="email" label={t('signIn.email')}>
          {(field) => (
            <TextInput type="email" autoComplete="username" inputMode="email" spellCheck={false} {...field} />
          )}
        </FormField>
        <FormField control={form.control} name="password" label={t('signIn.password')}>
          {(field) => <PasswordInput autoComplete="current-password" {...field} />}
        </FormField>
        {error !== undefined && <AlertBanner severity="error">{problemMessage(t, error)}</AlertBanner>}
        <Button type="submit" pending={login.isPending || form.formState.isSubmitting}>
          {t('signIn.submit')}
        </Button>
      </Form>
      <p className="text-body">
        <Link
          to="/password/forgot"
          className="font-medium text-primary underline underline-offset-4 hover:no-underline"
        >
          {t('signIn.forgot')}
        </Link>
      </p>
    </>
  );
}
