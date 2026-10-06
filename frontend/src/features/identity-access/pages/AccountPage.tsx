import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useAppForm } from '@/components/app/use-app-form';
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
import { PageHeader } from '@/components/app/PageHeader';
import { PasswordInput } from '@/components/app/PasswordInput';
import { DescriptionList } from '@/components/app/DescriptionList';
import { RecoveryCodeList } from '@/components/app/RecoveryCodeList';
import { SectionCard } from '@/components/app/SectionCard';
import { SectionGrid } from '@/components/app/SectionGrid';
import { useSaveNotice } from '@/components/app/save-notice';
import { explainFailure } from '@/components/app/confirm-failure';
import { TextInput } from '@/components/app/TextInput';
import { NotificationsSection } from '@/features/notifications/NotificationsSection';
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
  const notify = useSaveNotice();
  const form = useAppForm<PasswordValues>({
    resolver: zodResolver(passwordSchema),
    defaultValues: { current: '', password: '', confirm: '' },
  });
  const rules = passwordRuleMessages(t, change.error);

  const onSubmit = async (values: PasswordValues): Promise<void> => {
    try {
      await change.mutateAsync({ data: { currentPassword: values.current, newPassword: values.password } });
      form.reset();
      // Announced without moving focus; a banner mounted already filled is often not read (SC 4.1.3).
      notify(t('account.passwordChanged'));
    } catch {
      form.setValue('current', '');
      form.setFocus('current');
    }
  };

  return (
    <SectionCard title={t('account.passwordTitle')} description={t('password.rules')}>
      <Form form={form} onSubmit={onSubmit}>
        <FormField control={form.control} name="current" label={t('account.currentPassword')} width="name">
          {(field) => <PasswordInput autoComplete="current-password" {...field} />}
        </FormField>
        <FormField control={form.control} name="password" label={t('account.newPassword')} width="name">
          {(field) => <PasswordInput autoComplete="new-password" {...field} />}
        </FormField>
        <FormField control={form.control} name="confirm" label={t('account.confirmPassword')} width="name">
          {(field) => <PasswordInput autoComplete="new-password" {...field} />}
        </FormField>
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
        <Button type="submit" variant="secondary" className="self-start" pending={change.isPending}>
          {t('account.changePassword')}
        </Button>
      </Form>
    </SectionCard>
  );
}

function RecoveryCodesSection({ codesLeft }: { codesLeft: number }) {
  const { t } = useTranslation('identity');
  const queryClient = useQueryClient();
  const regenerate = useRegenerateRecoveryCodes();
  const [codes, setCodes] = useState<string[]>();
  const form = useAppForm<CodeValues>({ resolver: zodResolver(codeSchema), defaultValues: { code: '' } });

  const onSubmit = async ({ code }: CodeValues): Promise<void> => {
    let response: Awaited<ReturnType<typeof regenerate.mutateAsync>>;
    try {
      response = await regenerate.mutateAsync({ data: { code: digitsOnly(code) } });
    } catch {
      form.setValue('code', '');
      form.setFocus('code');
      return;
    }
    // Outside the try: an unreadable answer is reported by the form, never swallowed.
    setCodes(responseData(response).recoveryCodes);
    form.reset();
    // Only the count of codes left changes; a failed refresh just shows the old count.
    await queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY });
  };

  return (
    <SectionCard
      title={t('account.recoveryTitle')}
      description={t('account.recoveryLeft', { count: codesLeft })}
    >
      <Form form={form} onSubmit={onSubmit} requiredNote={false}>
        {/* Without codes, a lost phone locks the account: a warning, not a quiet count (UI audit). */}
        {codesLeft === 0 && !codes && (
          <AlertBanner severity="warning" live={false}>
            {t('account.recoveryNoneLeft')}
          </AlertBanner>
        )}
        {codes ? (
          <>
            <AlertBanner severity="success" focusOnMount>
              {t('account.regenerated')}
            </AlertBanner>
            <RecoveryCodeList codes={codes} />
          </>
        ) : (
          <FormField control={form.control} name="code" label={t('account.recoveryCode')} width="id">
            {(field) => (
              <TextInput autoComplete="one-time-code" inputMode="numeric" spellCheck={false} {...field} />
            )}
          </FormField>
        )}
        {regenerate.isError && (
          <AlertBanner severity="error">{problemMessage(t, regenerate.error)}</AlertBanner>
        )}
        {!codes && (
          <Button type="submit" variant="secondary" className="self-start" pending={regenerate.isPending}>
            {t('account.regenerate')}
          </Button>
        )}
      </Form>
    </SectionCard>
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
      <SectionGrid>
        <SectionCard title={t('account.profile')} description={t('account.languageDescription')}>
          <DescriptionList
            items={[
              { term: t('account.name'), value: account.name },
              { term: t('account.email'), value: <span className="break-all">{account.email}</span> },
              { term: t('account.role'), value: t(`roles.${account.role}`) },
            ]}
          />
        </SectionCard>
        <ChangePasswordForm />
        <RecoveryCodesSection codesLeft={account.recoveryCodesLeft} />
        <NotificationsSection />
        <SectionCard
          title={t('account.signOutEverywhereTitle')}
          description={t('account.signOutEverywhereDescription')}
        >
          <ConfirmDialog
            title={t('account.signOutEverywhereConfirmTitle')}
            description={t('account.signOutEverywhereConfirmDescription')}
            confirmLabel={t('account.signOutEverywhere')}
            onConfirm={async () => {
              await explainFailure(
                () => signOutEverywhere.mutateAsync(),
                (error) => problemMessage(t, error),
              );
              await forgetSession(); // The server has already ended this session too.
            }}
            trigger={
              <Button type="button" variant="destructive" className="self-start">
                {t('account.signOutEverywhere')}
              </Button>
            }
          />
        </SectionCard>
      </SectionGrid>
    </>
  );
}
