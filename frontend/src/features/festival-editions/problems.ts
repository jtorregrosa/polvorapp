import type { TFunction } from 'i18next';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiProblemError } from '@/api/http';

/** Problem codes the editions API returns (design D4); each has a text in `editions:errors`. */
export const EDITIONS_PROBLEM_CODES = [
  'editions.notFound',
  'editions.yearTaken',
  'editions.modified',
  'editions.invalidTransition',
  'editions.anotherInProgress',
  'editions.incomplete',
  'editions.ordersOpen',
  'editions.notInProgress',
  'editions.notDraft',
  'editions.tooManyMilestones',
  'editions.milestoneNotFound',
  'editions.busy',
] as const;

export type EditionsProblemCode = (typeof EDITIONS_PROBLEM_CODES)[number];

/** Field reasons the API reports in a `validation` problem; each has a text in `editions:validation`. */
const FIELD_REASONS = [
  'required',
  'invalid',
  'tooLong',
  'outOfRange',
  'outsideYear',
  'beforeStart',
  'beforeOpen',
  'afterFestival',
  'decimals',
  'notFound',
  'notRentable',
  'tooMany',
] as const;

type FieldReason = (typeof FIELD_REASONS)[number];

/** The fields `editions.incomplete` can list as missing, in form order. */
const MISSING_FIELDS = [
  'festivalStartsOn',
  'festivalEndsOn',
  'ordersOpenOn',
  'ordersCloseOn',
  'prices.powderPerKg',
  'prices.capsBox',
  'prices.weaponRental',
  'prices.flaskRental',
] as const;

type MissingField = (typeof MISSING_FIELDS)[number];

const isFieldReason = (reason: string): reason is FieldReason =>
  (FIELD_REASONS as readonly string[]).includes(reason);

const isKnownCode = (code: string): code is EditionsProblemCode =>
  (EDITIONS_PROBLEM_CODES as readonly string[]).includes(code);

export function problemCode(error: unknown): string | undefined {
  return error instanceof ApiProblemError ? error.problem?.code : undefined;
}

/** An extension of the problem: `missing` or `inProgressYear`. */
function extension(error: unknown, name: 'missing' | 'inProgressYear'): unknown {
  return error instanceof ApiProblemError ? error.problem?.[name] : undefined;
}

/**
 * A translated, user-facing message for a failed editions request. An incomplete edition lists
 * its missing fields in words; another edition in progress is named by its year.
 */
export function problemMessage(
  t: TFunction<'editions'>,
  error: unknown,
  list: (items: string[]) => string,
): string {
  const code = problemCode(error);
  if (code === 'editions.incomplete') {
    const missing = extension(error, 'missing');
    const fields = (Array.isArray(missing) ? missing : [])
      .filter((field): field is MissingField => (MISSING_FIELDS as readonly unknown[]).includes(field))
      .map((field) => t(`fields.${field}`));
    return fields.length > 0
      ? t('errors.editions.incomplete', { fields: list(fields) })
      : t('errors.generic');
  }
  if (code === 'editions.anotherInProgress') {
    const year = extension(error, 'inProgressYear');
    return typeof year === 'number'
      ? t('errors.editions.anotherInProgress', { year })
      : t('errors.editions.anotherInProgressUnknown');
  }
  if (code && isKnownCode(code)) {
    return t(`errors.${code}`);
  }
  return code === 'validation' ? t('errors.validation') : t('errors.generic');
}

/** The known reason the API gives for `field` in a `validation` problem, if any. */
export function validationReason(error: unknown, field: string): FieldReason | undefined {
  const errors =
    error instanceof ApiProblemError && problemCode(error) === 'validation'
      ? error.problem?.errors
      : undefined;
  const reason = errors && !Array.isArray(errors) ? errors[field] : undefined;
  return reason !== undefined && isFieldReason(reason) ? reason : undefined;
}

/**
 * Puts the API's field errors on the form's fields (a `validation` problem, and a conflict about
 * one field such as `editions.yearTaken` on the year). Returns false when nothing was put on a
 * field: show {@link problemMessage} instead.
 */
export function applyFieldErrors<TValues extends FieldValues>(
  error: unknown,
  fields: readonly Path<TValues>[],
  setError: UseFormSetError<TValues>,
  conflicts: Partial<Record<EditionsProblemCode, Path<TValues>>> = {},
): boolean {
  const code = problemCode(error);
  const conflictField = code && isKnownCode(code) ? conflicts[code] : undefined;
  if (conflictField) {
    setError(conflictField, { type: 'server', message: `editions:errors.${code}` });
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
      const known = isFieldReason(reason) ? reason : 'invalid';
      setError(field, { type: 'server', message: `editions:validation.${known}` });
      applied = true;
    }
  }
  return applied;
}

/** Zod messages are translation keys; FormField translates them (`editions:` prefixed). */
export const messages = {
  required: 'editions:validation.required',
  invalid: 'editions:validation.invalid',
  tooLong: 'editions:validation.tooLong',
  date: 'editions:validation.date',
  yearRange: 'editions:validation.yearRange',
  outsideYear: 'editions:validation.outsideYear',
  beforeStart: 'editions:validation.beforeStart',
  beforeOpen: 'editions:validation.beforeOpen',
  afterFestival: 'editions:validation.afterFestival',
  money: 'editions:validation.money',
  moneyRange: 'editions:validation.moneyRange',
  decimals: 'editions:validation.decimals',
} as const;
