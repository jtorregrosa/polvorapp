import { zodResolver } from '@hookform/resolvers/zod';
import { useAppForm } from '@/components/app/use-app-form';
import { useTranslation } from 'react-i18next';
import type { z } from 'zod';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { Form, FormField } from '@/components/app/FormField';
import { PasswordInput } from '@/components/app/PasswordInput';
import { newPasswordFields, passwordRuleMessages, problemMessage } from '../problems';

type Values = z.infer<typeof newPasswordFields>;

export interface NewPasswordFormProps {
  passwordLabel: string;
  confirmLabel: string;
  submitLabel: string;
  onSubmit: (password: string) => Promise<void>;
  isPending: boolean;
  error: unknown;
}

/** Choose a password, typed twice, with the rules shown up front and the API's refusals listed. */
export function NewPasswordForm({
  passwordLabel,
  confirmLabel,
  submitLabel,
  onSubmit,
  isPending,
  error,
}: NewPasswordFormProps) {
  const { t } = useTranslation('identity');
  const form = useAppForm<Values>({
    resolver: zodResolver(newPasswordFields),
    defaultValues: { password: '', confirm: '' },
  });
  const rules = passwordRuleMessages(t, error);

  return (
    <Form form={form} onSubmit={({ password }) => onSubmit(password)}>
      <FormField
        control={form.control}
        name="password"
        label={passwordLabel}
        description={t('password.rules')}
      >
        {(field) => <PasswordInput autoComplete="new-password" {...field} />}
      </FormField>
      <FormField control={form.control} name="confirm" label={confirmLabel}>
        {(field) => <PasswordInput autoComplete="new-password" {...field} />}
      </FormField>
      {error !== null && error !== undefined && (
        <AlertBanner severity="error">
          {problemMessage(t, error)}
          {rules.length > 0 && (
            <ul className="mt-1 list-disc pl-5">
              {rules.map((rule) => (
                <li key={rule}>{rule}</li>
              ))}
            </ul>
          )}
        </AlertBanner>
      )}
      <Button type="submit" pending={isPending || form.formState.isSubmitting}>
        {submitLabel}
      </Button>
    </Form>
  );
}
