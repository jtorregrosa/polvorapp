import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import {
  useChangePassword,
  useRegenerateRecoveryCodes,
  useSignOutEverywhere,
} from '@/api/generated/account/account';
import { responseData } from '@/api/http';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { Form, FormField } from '@/components/app/FormField';
import { FormSection } from '@/components/app/FormSection';
import { PageHeader } from '@/components/app/PageHeader';
import { PasswordInput } from '@/components/app/PasswordInput';
import { RecoveryCodeList } from '@/components/app/RecoveryCodeList';
import { TextInput } from '@/components/app/TextInput';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import {
  digitsOnly,
  messages,
  passwordRuleMessages,
  problemMessage,
  totpField,
  MIN_PASSWORD_LENGTH,
} from '../problems';
import { SESSION_QUERY_KEY, useForgetSession, useSession } from '../session';

const passwordSchema = z
  .object({
    current: z.string().min(1, messages.required),
    password: z.string().min(MIN_PASSWORD_LENGTH, messages.passwordLength),
    confirm: z.string().min(1, messages.required),
  })
  .refine((value) => value.password === value.confirm, {
    path: ['confirm'],
    message: messages.passwordMismatch,
  });
type PasswordValues = z.infer<typeof passwordSchema>;

const codeSchema = z.object({ code: totpField });
type CodeValues = z.infer<typeof codeSchema>;

function ChangePasswordForm() {
  const { t } = useTranslation('identity');
  const change = useChangePassword();
  const form = useForm<PasswordValues>({
    resolver: zodResolver(passwordSchema),
    defaultValues: { current: '', password: '', confirm: '' },
  });
  const rules = passwordRuleMessages(t, change.error);

  const onSubmit = async (values: PasswordValues): Promise<void> => {
    try {
      await change.mutateAsync({ data: { currentPassword: values.current, newPassword: values.password } });
      form.reset();
    } catch {
      form.setValue('current', '');
      form.setFocus('current');
    }
  };

  return (
    <Form form={form} onSubmit={onSubmit}>
      <FormSection title={t('account.passwordTitle')} description={t('password.rules')}>
        <FormField control={form.control} name="current" label={t('account.currentPassword')} required>
          {(field) => <PasswordInput autoComplete="current-password" {...field} />}
        </FormField>
        <FormField control={form.control} name="password" label={t('account.newPassword')} required>
          {(field) => <PasswordInput autoComplete="new-password" {...field} />}
        </FormField>
        <FormField control={form.control} name="confirm" label={t('account.confirmPassword')} required>
          {(field) => <PasswordInput autoComplete="new-password" {...field} />}
        </FormField>
      </FormSection>
      {change.isSuccess && <AlertBanner severity="success">{t('account.passwordChanged')}</AlertBanner>}
      {change.isError && (
        <AlertBanner severity="error">
          {problemMessage(t, change.error)}
          {rules.length > 0 && (
            <ul className="mt-1 list-disc pl-5">
              {rules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          )}
        </AlertBanner>
      )}
      <Button type="submit" variant="secondary" pending={change.isPending}>
        {t('account.changePassword')}
      </Button>
    </Form>
  );
}

function RecoveryCodesSection({ codesLeft }: { codesLeft: number }) {
  const { t } = useTranslation('identity');
  const queryClient = useQueryClient();
  const regenerate = useRegenerateRecoveryCodes();
  const [codes, setCodes] = useState<string[]>();
  const form = useForm<CodeValues>({ resolver: zodResolver(codeSchema), defaultValues: { code: '' } });

  const onSubmit = async ({ code }: CodeValues): Promise<void> => {
    try {
      const response = await regenerate.mutateAsync({ data: { code: digitsOnly(code) } });
      setCodes(responseData(response).recoveryCodes);
    } catch {
      form.setValue('code', '');
      form.setFocus('code');
      return;
    }
    form.reset();
    // Only the count of codes left changes; a failed refresh just shows the old count.
    await queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY });
  };

  return (
    <Form form={form} onSubmit={onSubmit} requiredNote={false}>
      <FormSection
        title={t('account.recoveryTitle')}
        description={t('account.recoveryLeft', { count: codesLeft })}
      >
        {codes ? (
          <>
            <AlertBanner severity="success" focusOnMount>
              {t('account.regenerated')}
            </AlertBanner>
            <RecoveryCodeList codes={codes} />
          </>
        ) : (
          <FormField control={form.control} name="code" label={t('account.recoveryCode')} required>
            {(field) => <TextInput autoComplete="one-time-code" inputMode="numeric" {...field} />}
          </FormField>
        )}
      </FormSection>
      {regenerate.isError && (
        <AlertBanner severity="error">{problemMessage(t, regenerate.error)}</AlertBanner>
      )}
      {!codes && (
        <Button type="submit" variant="secondary" pending={regenerate.isPending}>
          {t('account.regenerate')}
        </Button>
      )}
    </Form>
  );
}

/** Spec "Account self-service". */
export function AccountPage() {
  const { t } = useTranslation('identity');
  useDocumentTitle(t('account.title'));
  const session = useSession();
  const signOutEverywhere = useSignOutEverywhere();
  const forgetSession = useForgetSession();
  const account = session.account;
  if (!account) {
    return null;
  }

  return (
    <>
      <PageHeader title={t('account.title')} description={t('account.description')} />
      <div className="grid max-w-2xl gap-10">
        <section aria-labelledby="account-profile" className="grid gap-2">
          <h2 id="account-profile" className="text-lg font-semibold">
            {t('account.profile')}
          </h2>
          <dl className="grid gap-2 text-sm">
            <div className="flex flex-wrap gap-x-3">
              <dt className="text-muted-foreground">{t('account.name')}</dt>
              <dd>{account.name}</dd>
            </div>
            <div className="flex flex-wrap gap-x-3">
              <dt className="text-muted-foreground">{t('account.email')}</dt>
              <dd className="break-all">{account.email}</dd>
            </div>
            <div className="flex flex-wrap gap-x-3">
              <dt className="text-muted-foreground">{t('account.role')}</dt>
              <dd>{t(`roles.${account.role}`)}</dd>
            </div>
          </dl>
          <p className="text-sm text-muted-foreground">{t('account.languageDescription')}</p>
        </section>
        <ChangePasswordForm />
        <RecoveryCodesSection codesLeft={account.recoveryCodesLeft} />
        <section aria-labelledby="account-everywhere" className="grid gap-3">
          <h2 id="account-everywhere" className="text-lg font-semibold">
            {t('account.signOutEverywhereTitle')}
          </h2>
          <p className="text-sm text-muted-foreground">{t('account.signOutEverywhereDescription')}</p>
          <ConfirmDialog
            title={t('account.signOutEverywhereConfirmTitle')}
            description={t('account.signOutEverywhereConfirmDescription')}
            confirmLabel={t('account.signOutEverywhere')}
            onConfirm={async () => {
              await signOutEverywhere.mutateAsync();
              await forgetSession(); // The server has already ended this session too.
            }}
            trigger={
              <Button type="button" variant="destructive" className="justify-self-start">
                {t('account.signOutEverywhere')}
              </Button>
            }
          />
        </section>
      </div>
    </>
  );
}
