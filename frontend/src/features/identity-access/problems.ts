import type { TFunction } from 'i18next';
import { z } from 'zod';
import { ApiProblemError } from '@/api/http';

/** Problem codes the identity API returns (design D8); each has a text in `identity:errors`. */
const KNOWN_CODES = new Set([
  'auth.invalidCredentials',
  'auth.invalidCode',
  'auth.lockedOut',
  'auth.stepExpired',
  'auth.unauthenticated',
  'auth.invalidLink',
  'auth.invalidPassword',
  'account.wrongCurrentPassword',
  'users.emailTaken',
  'users.lastAdmin',
  'users.notInvited',
  'users.notEnrolled',
  'users.notFound',
  'email.sendFailed',
] as const);

type KnownCode = typeof KNOWN_CODES extends Set<infer T> ? T : never;

export function problemCode(error: unknown): string | undefined {
  return error instanceof ApiProblemError ? error.problem?.code : undefined;
}

/** A translated, user-facing message for a failed request. */
export function problemMessage(t: TFunction<'identity'>, error: unknown): string {
  if (error instanceof ApiProblemError) {
    const code = error.problem?.code;
    if (code && KNOWN_CODES.has(code as KnownCode)) {
      return t(`errors.${code as KnownCode}`);
    }
    if (code === 'validation') {
      return t('errors.validation');
    }
    if (error.status === 429) {
      return t('errors.tooManyRequests');
    }
  }
  return t('errors.generic');
}

/** The password rules the API rejected (`auth.invalidPassword`), translated. */
export function passwordRuleMessages(t: TFunction<'identity'>, error: unknown): string[] {
  const errors = error instanceof ApiProblemError ? error.problem?.errors : undefined;
  if (!Array.isArray(errors)) {
    return [];
  }
  const rules = ['PasswordTooShort', 'PasswordTooLong', 'PasswordIsEmail', 'PasswordTooCommon'] as const;
  return rules.filter((rule) => errors.includes(rule)).map((rule) => t(`passwordErrors.${rule}`));
}

/** Zod messages are translation keys; FormField translates them (`identity:` prefixed). */
export const messages = {
  required: 'identity:validation.required',
  email: 'identity:validation.email',
  code: 'identity:validation.code',
  passwordLength: 'identity:validation.passwordLength',
  passwordMismatch: 'identity:validation.passwordMismatch',
  nameTooLong: 'identity:validation.nameTooLong',
  choice: 'identity:validation.choice',
} as const;

export const MIN_PASSWORD_LENGTH = 12;

export const emailField = z.string().trim().min(1, messages.required).pipe(z.email(messages.email));

export const requiredText = z.string().trim().min(1, messages.required);

/** Six digits; a space or dash in the middle, as authenticator apps show them, is accepted. */
export const totpField = z.string().regex(/^\s*\d{3}[\s-]?\d{3}\s*$/, messages.code);

/** The digits of a code as the API expects them. */
export const digitsOnly = (code: string): string => code.replace(/\D/g, '');

/** A new password typed twice (server rules beyond the length are reported by the API). */
export const newPasswordFields = z
  .object({
    password: z.string().min(MIN_PASSWORD_LENGTH, messages.passwordLength),
    confirm: z.string().min(1, messages.required),
  })
  .refine((value) => value.password === value.confirm, {
    path: ['confirm'],
    message: messages.passwordMismatch,
  });
