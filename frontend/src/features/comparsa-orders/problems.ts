import type { TFunction } from 'i18next';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiProblemError } from '@/api/http';

/** Problem codes the orders API returns (design D6); each has a text in `orders:errors`. */
export const ORDERS_PROBLEM_CODES = [
  'orders.notFound',
  'orders.alreadyPrepared',
  'orders.editionNotStarted',
  'orders.comparsaInactive',
  'orders.closed',
  'orders.validated',
  'orders.invalidTransition',
  'orders.modified',
  'orders.entriesInvalid',
  'orders.alreadyInEdition',
  'entries.notFound',
  'entries.modified',
  'orders.busy',
] as const;

export type OrdersProblemCode = (typeof ORDERS_PROBLEM_CODES)[number];

/**
 * Refusals that mean the order on screen is out of date: the page reloads it and shows it in its
 * new state (spec: Orders screens).
 */
export const RELOAD_CODES: readonly OrdersProblemCode[] = [
  'orders.closed',
  'orders.validated',
  'orders.modified',
  'orders.invalidTransition',
  'entries.modified',
  'entries.notFound',
];

/** Field reasons the API reports in a `validation` problem; each has a text in `orders:validation`. */
const FIELD_REASONS = [
  'required',
  'invalid',
  'tooLong',
  'outOfRange',
  'reserve',
  'notOwned',
  'notOffered',
  'ownWeapon',
  'notFound',
  'inactive',
  'lenderRegistered',
  'checkLetter',
] as const;

type FieldReason = (typeof FIELD_REASONS)[number];

const isFieldReason = (reason: string): reason is FieldReason =>
  (FIELD_REASONS as readonly string[]).includes(reason);

const isKnownCode = (code: string): code is OrdersProblemCode =>
  (ORDERS_PROBLEM_CODES as readonly string[]).includes(code);

export function problemCode(error: unknown): string | undefined {
  return error instanceof ApiProblemError ? error.problem?.code : undefined;
}

/** Whether the refusal means the order on screen is out of date and must be reloaded. */
export function needsReload(error: unknown): boolean {
  const code = problemCode(error);
  return code !== undefined && isKnownCode(code) && RELOAD_CODES.includes(code);
}

/** A translated, user-facing message for a failed orders request. */
export function problemMessage(t: TFunction<'orders'>, error: unknown): string {
  const code = problemCode(error);
  if (code && isKnownCode(code)) {
    return t(`errors.${code}`);
  }
  return code === 'validation' ? t('errors.validation') : t('errors.generic');
}

/** The entries an `orders.entriesInvalid` refusal names, with their reasons. */
export function invalidEntries(error: unknown): { entryId: string; reasons: string[] }[] {
  const entries = error instanceof ApiProblemError ? error.problem?.entries : undefined;
  if (!Array.isArray(entries)) return [];
  return entries.flatMap((entry: unknown) => {
    if (typeof entry !== 'object' || entry === null) return [];
    const { entryId, reasons } = entry as { entryId?: unknown; reasons?: unknown };
    return typeof entryId === 'string' && Array.isArray(reasons)
      ? [{ entryId, reasons: reasons.filter((reason): reason is string => typeof reason === 'string') }]
      : [];
  });
}

/**
 * Puts the API's field errors on the form's fields. `fields` maps API field names (such as
 * `loan.nationalId`) to the form's. Returns false when nothing was put on a field: show
 * {@link problemMessage} instead.
 */
export function applyFieldErrors<TValues extends FieldValues>(
  error: unknown,
  fields: Readonly<Record<string, Path<TValues>>>,
  setError: UseFormSetError<TValues>,
): boolean {
  const errors =
    error instanceof ApiProblemError && problemCode(error) === 'validation'
      ? error.problem?.errors
      : undefined;
  if (!errors || Array.isArray(errors)) {
    return false;
  }

  let applied = false;
  for (const [apiField, formField] of Object.entries(fields)) {
    const reason = errors[apiField];
    if (reason) {
      const known = isFieldReason(reason) ? reason : 'invalid';
      setError(formField, { type: 'server', message: `orders:validation.${known}` });
      applied = true;
    }
  }
  return applied;
}

/** Zod messages are translation keys; FormField translates them (`orders:` prefixed). */
export const messages = {
  required: 'orders:validation.required',
  choice: 'orders:validation.choice',
  invalid: 'orders:validation.invalid',
  tooLong: 'orders:validation.tooLong',
  outOfRange: 'orders:validation.outOfRange',
  capsType: 'orders:validation.capsType',
  reserve: 'orders:validation.reserve',
  checkLetter: 'orders:validation.checkLetter',
  ownWeapon: 'orders:validation.ownWeapon',
  lookUpFirst: 'orders:validation.lookUpFirst',
  attestation: 'orders:validation.attestation',
} as const;
