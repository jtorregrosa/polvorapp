import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useAppForm } from '@/components/app/use-app-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { useLoginSecondFactor } from '@/api/generated/auth/auth';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { CheckboxField } from '@/components/app/CheckboxField';
import { Form, FormField } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { TextInput } from '@/components/app/TextInput';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { digitsOnly, problemCode, problemMessage, totpField } from '../problems';
import { safeReturnTo, signInPath, useCompleteSignIn } from '../session';

const schema = z.object({ code: totpField });
type Values = z.infer<typeof schema>;

/** Spec "Sign-in with two-factor authentication" and "Remembered devices": the code step. */
export function SecondFactorPage() {
  const { t } = useTranslation('identity');
  useDocumentTitle(t('secondFactor.title'));
  const [search] = useSearchParams();
  const returnTo = safeReturnTo(search.get('returnTo'));
  const navigate = useNavigate();
  const completeSignIn = useCompleteSignIn();
  const secondFactor = useLoginSecondFactor();
  const [rememberDevice, setRememberDevice] = useState(false);
  const [error, setError] = useState<unknown>();
  const [signedIn, setSignedIn] = useState(false);
  const form = useAppForm<Values>({ resolver: zodResolver(schema), defaultValues: { code: '' } });

  /** Loads the session and goes on; a failure leaves the person here, signed in, to retry. */
  const finish = async (): Promise<void> => {
    try {
      await completeSignIn();
    } catch (failure) {
      setError(failure);
      return;
    }
    await navigate(returnTo, { replace: true });
  };

  const onSubmit = async ({ code }: Values): Promise<void> => {
    setError(undefined);
    if (signedIn) {
      await finish(); // The code was accepted before; only loading the session failed.
      return;
    }
    try {
      await secondFactor.mutateAsync({ data: { code: digitsOnly(code), rememberDevice } });
    } catch (failure) {
      if (problemCode(failure) === 'auth.stepExpired') {
        await navigate(signInPath(returnTo, '', 'stepExpired'), { replace: true });
        return;
      }
      setError(failure);
      form.setValue('code', '');
      form.setFocus('code');
      return;
    }
    setSignedIn(true);
    await finish();
  };

  return (
    <>
      <PageHeader title={t('secondFactor.title')} description={t('secondFactor.description')} />
      <Form form={form} onSubmit={onSubmit} requiredNote={false}>
        <FormField control={form.control} name="code" label={t('secondFactor.code')}>
          {(field) => (
            <TextInput autoComplete="one-time-code" inputMode="numeric" spellCheck={false} {...field} />
          )}
        </FormField>
        <CheckboxField
          label={t('secondFactor.rememberDevice')}
          description={t('secondFactor.rememberDeviceHelp')}
          checked={rememberDevice}
          onCheckedChange={setRememberDevice}
        />
        {error !== undefined && <AlertBanner severity="error">{problemMessage(t, error)}</AlertBanner>}
        <Button type="submit" pending={secondFactor.isPending || form.formState.isSubmitting}>
          {t('secondFactor.submit')}
        </Button>
      </Form>
      <p className="text-body">
        <Link
          to={`/login/recovery-code?returnTo=${encodeURIComponent(returnTo)}`}
          className="font-medium text-primary underline underline-offset-4 hover:no-underline"
        >
          {t('secondFactor.useRecoveryCode')}
        </Link>
      </p>
      <p className="text-help text-muted-foreground">{t('secondFactor.lostEverything')}</p>
    </>
  );
}
