import type { TFunction } from 'i18next';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { z } from 'zod';
import { ApiProblemError } from '@/api/http';

/** Problem codes the catalogue API returns (design D4); each has a text in `catalog:errors`. */
export const CATALOG_PROBLEM_CODES = [
  'comparsas.notFound',
  'comparsas.nameTaken',
  'comparsas.inUse',
  'assignments.userNotFound',
  'assignments.notFiringChief',
  'assignments.userDeactivated',
  'assignments.comparsaInactive',
  'weaponModels.notFound',
  'weaponModels.labelTaken',
  'weaponModels.combinationTaken',
  'weaponModels.inUse',
  'logos.notFound',
  'storage.unavailable',
  'catalog.busy',
] as const;

type CatalogProblemCode = (typeof CATALOG_PROBLEM_CODES)[number];

/** Field reasons the API reports in a `validation` problem; each has a text in `catalog:validation`. */
const FIELD_REASONS = ['required', 'invalid', 'tooLong'] as const;

type FieldReason = (typeof FIELD_REASONS)[number];

const isKnownCode = (code: string): code is CatalogProblemCode =>
  (CATALOG_PROBLEM_CODES as readonly string[]).includes(code);

export function problemCode(error: unknown): string | undefined {
  return error instanceof ApiProblemError ? error.problem?.code : undefined;
}

/** A translated, user-facing message for a failed catalogue request. */
export function problemMessage(t: TFunction<'catalog'>, error: unknown): string {
  const code = problemCode(error);
  if (code && isKnownCode(code)) {
    return t(`errors.${code}`);
  }
  return code === 'validation' ? t('errors.validation') : t('errors.generic');
}

/** Reasons the API gives for a refused logo file (spec: Logo validation and processing). */
const LOGO_FILE_REASONS = ['required', 'tooLarge', 'unsupportedFormat', 'tooSmall', 'aspectRatio'] as const;

type LogoFileReason = (typeof LOGO_FILE_REASONS)[number];

const isLogoFileReason = (reason: string): reason is LogoFileReason =>
  (LOGO_FILE_REASONS as readonly string[]).includes(reason);

/**
 * A translated message for a failed logo upload or removal: the file's reason when there is one,
 * the image-upload rate limit (`429`, add-comparsa-logos D6), or the catalogue's problem text. A
 * `validation` problem without a known file reason is generic: the crop dialog has no fields.
 */
export function logoProblemMessage(t: TFunction<'catalog'>, error: unknown): string {
  if (!(error instanceof ApiProblemError)) {
    return t('errors.generic');
  }
  if (error.status === 429) {
    return t('errors.tooManyRequests');
  }
  if (problemCode(error) === 'validation') {
    const errors = error.problem?.errors;
    const reason = errors && !Array.isArray(errors) ? errors.file : undefined;
    return reason !== undefined && isLogoFileReason(reason)
      ? t(`validation.file.${reason}`)
      : t('errors.generic');
  }
  return problemMessage(t, error);
}

/**
 * Puts the API's field errors on the form's fields, so each one is shown (and the first one
 * focused) where it belongs: the fields of a `validation` problem, and a conflict that is about
 * one field (e.g. `comparsas.nameTaken` on the name). Returns false when nothing was put on a
 * field (show {@link problemMessage} instead).
 */
export function applyFieldErrors<TValues extends FieldValues>(
  error: unknown,
  fields: readonly Path<TValues>[],
  setError: UseFormSetError<TValues>,
  conflicts: Partial<Record<CatalogProblemCode, Path<TValues>>> = {},
): boolean {
  const code = problemCode(error);
  const conflictField = code && isKnownCode(code) ? conflicts[code] : undefined;
  if (conflictField) {
    setError(conflictField, { type: 'server', message: `catalog:errors.${code}` });
    return true;
  }

  const errors =
    error instanceof ApiProblemError && code === 'validation' ? error.problem?.errors : undefined;
  if (!errors || Array.isArray(errors)) {
    return false;
  }

  let applied = false;
  for (const field of fields) {
    const reason = errors[field];
    if (reason) {
      const known = (FIELD_REASONS as readonly string[]).includes(reason)
        ? (reason as FieldReason)
        : 'invalid';
      setError(field, { type: 'server', message: messages[known] });
      applied = true;
    }
  }
  return applied;
}

/** Zod messages are translation keys; FormField translates them (`catalog:` prefixed). */
export const messages = {
  required: 'catalog:validation.required',
  invalid: 'catalog:validation.invalid',
  tooLong: 'catalog:validation.tooLong',
  choice: 'catalog:validation.choice',
} as const;

/** The longest name or label the API accepts (Comparsa.NameMaxLength, WeaponModel.LabelMaxLength). */
export const MAX_TEXT_LENGTH = 100;

export const requiredText = z
  .string()
  .trim()
  .min(1, messages.required)
  .max(MAX_TEXT_LENGTH, messages.tooLong);
