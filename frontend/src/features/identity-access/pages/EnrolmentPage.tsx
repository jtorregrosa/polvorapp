import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { useConfirmEnrolment, useGetEnrolment } from '@/api/generated/auth/auth';
import type { EnrolmentResponse } from '@/api/generated/model';
import { responseData } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { PageHeader } from '@/components/app/PageHeader';
import { QrCode } from '@/components/app/QrCode';
import { TextInput } from '@/components/app/TextInput';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { digitsOnly, problemCode, problemMessage, totpField } from '../problems';
import { handOverRecoveryCodes } from '../recoveryCodeHandoff';
import { safeReturnTo, signInPath } from '../session';

const schema = z.object({ code: totpField });
type Values = z.infer<typeof schema>;

/** Spec "Mandatory two-factor enrolment": scan, confirm, then see the recovery codes once. */
export function EnrolmentPage() {
  const { t } = useTranslation('identity');
  useDocumentTitle(t('enrolment.title'));
  const [search] = useSearchParams();
  const returnTo = safeReturnTo(search.get('returnTo'));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  // The key must not change while the person scans it: never refetch.
  const enrolment = useGetEnrolment({
    query: { staleTime: Infinity, gcTime: 0, retry: false, refetchOnMount: false },
  });
  const confirm = useConfirmEnrolment();
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { code: '' } });

  const onSubmit = async ({ code }: Values): Promise<void> => {
    let codes: string[];
    try {
      const response = await confirm.mutateAsync({ data: { code: digitsOnly(code) } });
      codes = responseData(response).recoveryCodes;
    } catch (error) {
      if (problemCode(error) === 'auth.stepExpired') {
        await navigate(signInPath(returnTo, '', 'stepExpired'), { replace: true });
        return;
      }
      form.setValue('code', '');
      form.setFocus('code');
      return;
    }
    handOverRecoveryCodes(queryClient, codes);
    await navigate('/recovery-codes', { replace: true, state: { returnTo } });
  };

  if (enrolment.isError) {
    return (
      <>
        <PageHeader title={t('enrolment.title')} />
        <AlertBanner severity="error" className="mb-4">
          {problemMessage(t, enrolment.error)}
        </AlertBanner>
        <Button type="button" variant="secondary" onClick={() => void navigate('/login', { replace: true })}>
          {t('password.backToSignIn')}
        </Button>
      </>
    );
  }

  const details = enrolment.data?.data as EnrolmentResponse | undefined;
  return (
    <>
      <PageHeader title={t('enrolment.title')} description={t('enrolment.description')} />
      <ol className="mb-6 grid list-decimal gap-2 pl-5 text-sm">
        <li>{t('enrolment.step1')}</li>
        <li>{t('enrolment.step2')}</li>
        <li>{t('enrolment.step3')}</li>
      </ol>
      {details && (
        <div className="mb-6 grid justify-items-center gap-3">
          <QrCode value={details.authenticatorUri} label={t('enrolment.qrLabel')} />
          <p className="text-center text-sm text-muted-foreground">{t('enrolment.manualKey')}</p>
          <code className="rounded-md bg-muted px-3 py-2 text-center font-mono text-sm break-all">
            {details.sharedKey}
          </code>
        </div>
      )}
      <Form form={form} onSubmit={onSubmit} requiredNote={false}>
        <FormField control={form.control} name="code" label={t('enrolment.code')} required>
          {(field) => <TextInput autoComplete="one-time-code" inputMode="numeric" {...field} />}
        </FormField>
        {confirm.isError && <AlertBanner severity="error">{problemMessage(t, confirm.error)}</AlertBanner>}
        <Button type="submit" pending={confirm.isPending || form.formState.isSubmitting} disabled={!details}>
          {t('enrolment.submit')}
        </Button>
      </Form>
    </>
  );
}
