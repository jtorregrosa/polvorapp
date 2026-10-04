import type { TFunction } from 'i18next';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiProblemError } from '@/api/http';

/** Problem codes the distribution API returns; each has a text in `distribution:errors`. */
export const DISTRIBUTION_PROBLEM_CODES = [
  'distribution.notFound',
  'distribution.alreadyPlanned',
  'distribution.editionNotInProgress',
  'distribution.modified',
  'distribution.busy',
  'distribution.auditUnavailable',
  'proxies.notFound',
  'proxies.alreadyAuthorised',
  'proxies.proxyAbsent',
  'proxies.holderIsProxy',
] as const;

export type DistributionProblemCode = (typeof DISTRIBUTION_PROBLEM_CODES)[number];

/** Field reasons the API reports in a `validation` problem; each has a text in `distribution:fields`. */
const FIELD_REASONS = [
  'required',
  'invalid',
  'tooLong',
  'outOfEdition',
  'unknown',
  'duplicate',
  'notInOrder',
  'sameAsHolder',
  'nothingToCollect',
  'licenseInvalid',
  // A person erased on a GDPR request meanwhile (add-audit-privacy).
  'entryErased',
] as const;

type FieldReason = (typeof FIELD_REASONS)[number];

const isFieldReason = (reason: string): reason is FieldReason =>
  (FIELD_REASONS as readonly string[]).includes(reason);

const isKnownCode = (code: string): code is DistributionProblemCode =>
  (DISTRIBUTION_PROBLEM_CODES as readonly string[]).includes(code);

export function problemCode(error: unknown): string | undefined {
  return error instanceof ApiProblemError ? error.problem?.code : undefined;
}

/** Answers that mean the page shows outdated data: it is refreshed as well as told (their texts say so). */
const STALE_CODES: ReadonlySet<string> = new Set([
  'distribution.notFound',
  'distribution.alreadyPlanned',
  'distribution.editionNotInProgress',
  'distribution.modified',
  'proxies.notFound',
  'proxies.alreadyAuthorised',
  'proxies.proxyAbsent',
  'proxies.holderIsProxy',
]);

export function isStale(error: unknown): boolean {
  return STALE_CODES.has(problemCode(error) ?? '');
}

/** A translated, user-facing message for a failed distribution request. */
export function problemMessage(t: TFunction<'distribution'>, error: unknown): string {
  const code = problemCode(error);
  if (code && isKnownCode(code)) {
    return t(`errors.${code}`);
  }
  if (error instanceof ApiProblemError && error.status === 429) {
    return t('errors.tooMany');
  }
  return code === 'validation' ? t('errors.validation') : t('errors.generic');
}

/**
 * Puts the API's field errors on the form's fields: `fields` maps each API field name (e.g.
 * `slots[2].startsAt`) to the form's path, and `conflicts` a conflict about one field (e.g.
 * `proxies.alreadyAuthorised` on the holder). Returns false when nothing was put on a field: show
 * {@link problemMessage} instead.
 */
export function applyFieldErrors<TValues extends FieldValues>(
  error: unknown,
  fields: ReadonlyMap<string, Path<TValues>>,
  setError: UseFormSetError<TValues>,
  conflicts: Partial<Record<DistributionProblemCode, Path<TValues>>> = {},
): boolean {
  const code = problemCode(error);
  const conflictField = code && isKnownCode(code) ? conflicts[code] : undefined;
  if (conflictField) {
    setError(conflictField, { type: 'server', message: `distribution:errors.${code}` });
    return true;
  }

  const errors =
    error instanceof ApiProblemError && problemCode(error) === 'validation'
      ? error.problem?.errors
      : undefined;
  if (!errors || Array.isArray(errors)) {
    return false;
  }

  let applied = false;
  for (const [field, path] of fields) {
    const reason = errors[field];
    if (reason) {
      const known = isFieldReason(reason) ? reason : 'invalid';
      setError(path, { type: 'server', message: `distribution:fields.${known}` });
      applied = true;
    }
  }
  return applied;
}

/** The same field names in the API and the form. */
export const sameNames = <TValues extends FieldValues>(...names: Path<TValues>[]) =>
  new Map<string, Path<TValues>>(names.map((name) => [name, name]));
