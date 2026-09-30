import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { useLoginRecoveryCode } from '@/api/generated/auth/auth';
import { responseData } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { TextInput } from '@/components/app/TextInput';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemCode, problemMessage, requiredText } from '../problems';
import { safeReturnTo, signInPath, useCompleteSignIn } from '../session';

const schema = z.object({ code: requiredText });
type Values = z.infer<typeof schema>;

/** Few codes left: the person is told to generate new ones. */
const FEW_CODES_LEFT = 3;

/** After a recovery code: how many are left, then on to the page the person came for. */
function CodesLeft({ codesLeft, returnTo }: { codesLeft: number; returnTo: string }) {
  const { t } = useTranslation('identity');
  const navigate = useNavigate();
  const completeSignIn = useCompleteSignIn();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<unknown>();

  const onContinue = async (): Promise<void> => {
    setPending(true);
    setError(undefined);
    try {
      await completeSignIn();
    } catch (failure) {
      setError(failure); // Signed in, but the session could not be loaded: continuing retries.
      setPending(false);
      return;
    }
    await navigate(returnTo, { replace: true });
  };

  return (
    <>
      <PageHeader title={t('recovery.title')} focusOnMount />
      <AlertBanner severity={codesLeft <= FEW_CODES_LEFT ? 'warning' : 'success'} className="mb-6">
        {t('recovery.left', { count: codesLeft })}
      </AlertBanner>
      {error !== undefined && (
        <AlertBanner severity="error" className="mb-6">
          {problemMessage(t, error)}
        </AlertBanner>
      )}
      <Button type="button" pending={pending} onClick={() => void onContinue()}>
        {t('signIn.submit')}
      </Button>
    </>
  );
}

/** Spec "Sign-in with two-factor authentication", scenario "Recovery code". */
export function RecoveryCodePage() {
  const { t } = useTranslation('identity');
  useDocumentTitle(t('recovery.title'));
  const [search] = useSearchParams();
  const returnTo = safeReturnTo(search.get('returnTo'));
  const navigate = useNavigate();
  const recovery = useLoginRecoveryCode();
  const [codesLeft, setCodesLeft] = useState<number>();
  const [error, setError] = useState<unknown>();
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { code: '' } });

  const onSubmit = async ({ code }: Values): Promise<void> => {
    setError(undefined);
    try {
      const response = await recovery.mutateAsync({ data: { code: code.trim() } });
      setCodesLeft(responseData(response).recoveryCodesLeft);
    } catch (failure) {
      if (problemCode(failure) === 'auth.stepExpired') {
        await navigate(signInPath(returnTo, '', 'stepExpired'), { replace: true });
        return;
      }
      setError(failure);
      form.setValue('code', '');
      form.setFocus('code');
    }
  };

  if (codesLeft !== undefined) {
    return <CodesLeft codesLeft={codesLeft} returnTo={returnTo} />;
  }

  return (
    <>
      <PageHeader title={t('recovery.title')} description={t('recovery.description')} />
      <Form form={form} onSubmit={onSubmit} requiredNote={false}>
        <FormField control={form.control} name="code" label={t('recovery.code')} required>
          {(field) => (
            <TextInput
              autoComplete="one-time-code"
              autoCapitalize="characters"
              spellCheck={false}
              {...field}
            />
          )}
        </FormField>
        {error !== undefined && <AlertBanner severity="error">{problemMessage(t, error)}</AlertBanner>}
        <Button type="submit" pending={recovery.isPending || form.formState.isSubmitting}>
          {t('recovery.submit')}
        </Button>
      </Form>
      <p className="mt-6 text-sm">
        <Link
          to={`/login/second-factor?returnTo=${encodeURIComponent(returnTo)}`}
          className="font-medium text-primary underline-offset-4 hover:underline"
        >
          {t('recovery.useAuthenticator')}
        </Link>
      </p>
    </>
  );
}
