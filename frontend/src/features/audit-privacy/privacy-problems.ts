import type { TFunction } from 'i18next';
import { ApiProblemError } from '@/api/http';

/** Refusals of the GDPR requests (design D11); each has a text in `privacy:errors.privacy`. */
const PRIVACY_CODES = [
  'notFound',
  'busy',
  'storageUnavailable',
  'auditUnavailable',
  'selfErasure',
  'alreadyErased',
  'lastAdmin',
] as const;

type PrivacyCode = (typeof PRIVACY_CODES)[number];

/** Field reasons the API reports in a `validation` problem; each has a text in `privacy:fields`. */
const FIELD_REASONS = {
  nationalId: ['required', 'invalid', 'checkLetter'],
  reference: ['required', 'tooLong', 'invalid'],
} as const;

export type PrivacyField = keyof typeof FIELD_REASONS;

const isPrivacyCode = (code: string): code is PrivacyCode =>
  (PRIVACY_CODES as readonly string[]).includes(code);

const problemCode = (error: unknown): string | undefined =>
  error instanceof ApiProblemError ? error.problem?.code : undefined;

/** A translated message for a refused GDPR request. */
export function privacyProblemMessage(t: TFunction<'privacy'>, error: unknown): string {
  const code = problemCode(error)?.replace(/^privacy\./, '');
  if (code && isPrivacyCode(code)) {
    return t(`errors.privacy.${code}`);
  }
  if (error instanceof ApiProblemError && error.status === 429) return t('errors.tooManyRequests');
  return problemCode(error) === 'validation' ? t('errors.validation') : t('errors.generic');
}

/**
 * The message key of the API's error on `field`, for a form field's error (`privacy:fields.…`), or
 * nothing when the refusal is not about that field.
 */
export function privacyFieldError(error: unknown, field: PrivacyField): string | undefined {
  if (!(error instanceof ApiProblemError) || error.problem?.code !== 'validation') return undefined;
  const errors = error.problem.errors;
  const reason = errors && !Array.isArray(errors) ? errors[field] : undefined;
  if (typeof reason !== 'string') return undefined;
  const known = (FIELD_REASONS[field] as readonly string[]).includes(reason) ? reason : 'invalid';
  return `privacy:fields.${field}.${known}`;
}
