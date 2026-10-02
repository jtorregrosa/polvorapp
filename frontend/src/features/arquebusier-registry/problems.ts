import type { TFunction } from 'i18next';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiProblemError } from '@/api/http';

/** Problem codes the registry API returns (design D5); each has a text in `registry:errors`. */
export const REGISTRY_PROBLEM_CODES = [
  'arquebusiers.notFound',
  'arquebusiers.comparsaNotFound',
  'arquebusiers.comparsaInactive',
  'arquebusiers.sameComparsa',
  'arquebusiers.nationalIdTaken',
  'arquebusiers.federationIdTaken',
  'arquebusiers.modified',
  'ownedWeapons.notFound',
  'ownedWeapons.modelInactive',
  'ownedWeapons.guideTaken',
  'ownedWeapons.modified',
  'registry.busy',
  'photos.notFound',
  'photos.noLicense',
  'photos.modified',
  'storage.unavailable',
  'arquebusierImport.rowErrors',
  'arquebusierImport.conflict',
] as const;

export type RegistryProblemCode = (typeof REGISTRY_PROBLEM_CODES)[number];

/** Codes whose text tells a FiringChief to contact the Federation and an Admin to search instead. */
const BY_ROLE: ReadonlySet<RegistryProblemCode> = new Set([
  'arquebusiers.nationalIdTaken',
  'arquebusiers.federationIdTaken',
]);

/** Field reasons the API reports in a `validation` problem (design D5). */
const FIELD_REASONS = [
  'required',
  'invalid',
  'tooLong',
  'checkLetter',
  'future',
  'tooOld',
  'notAfterIssued',
  'datesWhilePending',
] as const;

type FieldReason = (typeof FIELD_REASONS)[number];

/** Zod and server messages are translation keys; FormField translates them. */
export const messages = {
  required: 'registry:validation.required',
  invalid: 'registry:validation.invalid',
  tooLong: 'registry:validation.tooLong',
  choice: 'registry:validation.choice',
  nationalIdInvalid: 'registry:validation.nationalIdInvalid',
  checkLetter: 'registry:validation.checkLetter',
  future: 'registry:validation.future',
  tooOld: 'registry:validation.tooOld',
  notAfterIssued: 'registry:validation.notAfterIssued',
  datesWhilePending: 'registry:validation.datesWhilePending',
  federationId: 'registry:validation.federationId',
  email: 'registry:validation.email',
  phone: 'registry:validation.phone',
  date: 'registry:validation.date',
  modelNotFound: 'registry:validation.modelNotFound',
} as const;

const isKnownCode = (code: string): code is RegistryProblemCode =>
  (REGISTRY_PROBLEM_CODES as readonly string[]).includes(code);

export function problemCode(error: unknown): string | undefined {
  return error instanceof ApiProblemError ? error.problem?.code : undefined;
}

/** A translated, user-facing message for a failed registry request. */
export function problemMessage(t: TFunction<'registry'>, error: unknown, { isAdmin = false } = {}): string {
  if (error instanceof ApiProblemError && error.status === 429) {
    return t('errors.tooManyRequests');
  }

  const code = problemCode(error);
  if (code && isKnownCode(code)) {
    return BY_ROLE.has(code) && isAdmin
      ? t(`errors.${code as 'arquebusiers.nationalIdTaken' | 'arquebusiers.federationIdTaken'}Admin`)
      : t(`errors.${code}`);
  }
  return code === 'validation' ? t('errors.validation') : t('errors.generic');
}

/** Reasons the API gives for a rejected photo file (spec: Photo validation and processing). */
export const PHOTO_FILE_REASONS = [
  'required',
  'tooLarge',
  'unsupportedFormat',
  'tooSmall',
  'aspectRatio',
] as const;

/** A translated message for a failed photo upload or removal, including the file's reason. */
export function photoProblemMessage(t: TFunction<'registry'>, error: unknown): string {
  const errors =
    error instanceof ApiProblemError && problemCode(error) === 'validation'
      ? error.problem?.errors
      : undefined;
  const reason = errors && !Array.isArray(errors) ? errors.file : undefined;
  if (reason !== undefined && (PHOTO_FILE_REASONS as readonly string[]).includes(reason)) {
    return t(`validation.file.${reason as (typeof PHOTO_FILE_REASONS)[number]}`);
  }
  return problemMessage(t, error);
}

/** Fields whose `invalid` has a more helpful text than "not valid": the one the form itself shows. */
const INVALID_BY_FIELD = {
  nationalId: messages.nationalIdInvalid,
  federationId: messages.federationId,
  email: messages.email,
  phone: messages.phone,
  birthDate: messages.date,
  trainingCompletedOn: messages.date,
  'license.issuedOn': messages.date,
  'license.expiresOn': messages.date,
} as const;

/** Reasons only an import reports on a row (add-registry-import D2). */
const IMPORT_REASONS = ['duplicateInFile', 'taken', 'cellError'] as const;

/** The translation key for a field reason the API reported on `field` (a form field or an import row). */
export function reasonMessage(field: string, reason: string): string {
  if ((IMPORT_REASONS as readonly string[]).includes(reason)) {
    return `registry:import.reasons.${reason}`;
  }
  if (field === 'weaponModelId' && reason === 'notFound') {
    return messages.modelNotFound;
  }
  if (reason === 'invalid' && Object.hasOwn(INVALID_BY_FIELD, field)) {
    return INVALID_BY_FIELD[field as keyof typeof INVALID_BY_FIELD];
  }
  return (FIELD_REASONS as readonly string[]).includes(reason)
    ? messages[reason as FieldReason]
    : messages.invalid;
}

/**
 * Puts the API's field errors on the form's fields, so each one is shown where it belongs (and the
 * first one focused): the fields of a `validation` problem, and a conflict about one field (e.g.
 * `arquebusiers.nationalIdTaken` on the DNI/NIE). `fields` maps the API's field names (such as
 * `license.issuedOn`) to the form's. Returns false unless every reported field was put on the form,
 * so the caller also shows its general message.
 */
export function applyFieldErrors<TValues extends FieldValues>(
  error: unknown,
  fields: Readonly<Partial<Record<string, Path<TValues>>>>,
  setError: UseFormSetError<TValues>,
  {
    conflicts = {},
    isAdmin = false,
  }: { conflicts?: Partial<Record<RegistryProblemCode, Path<TValues>>>; isAdmin?: boolean } = {},
): boolean {
  const code = problemCode(error);
  const conflictField = code && isKnownCode(code) ? conflicts[code] : undefined;
  if (code && conflictField) {
    const key =
      BY_ROLE.has(code as RegistryProblemCode) && isAdmin
        ? `registry:errors.${code}Admin`
        : `registry:errors.${code}`;
    setError(conflictField, { type: 'server', message: key });
    return true;
  }

  const errors =
    error instanceof ApiProblemError && code === 'validation' ? error.problem?.errors : undefined;
  if (!errors || Array.isArray(errors)) {
    return false;
  }

  let applied = false;
  let missed = false;
  for (const [apiField, reason] of Object.entries(errors)) {
    const field = Object.hasOwn(fields, apiField) ? fields[apiField] : undefined;
    if (!field) {
      missed = true;
    } else {
      // The form's error summary receives focus and links to each field (design D8).
      setError(field, { type: 'server', message: reasonMessage(apiField, reason) });
      applied = true;
    }
  }
  return applied && !missed;
}
