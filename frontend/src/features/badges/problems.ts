import { ApiProblemError } from '@/api/http';

/** The translated reasons of a refused badge sheet (`badges:errors.<key>`). */
export type BadgeErrorKey =
  | 'notFound'
  | 'nothingToPrint'
  | 'tooMany'
  | 'busy'
  | 'auditUnavailable'
  | 'storageUnavailable'
  | 'tooManyRequests'
  | 'failed';

/** Why a badge sheet was refused (spec: Badge screens). */
export type BadgeFailure =
  | { kind: 'reason'; key: BadgeErrorKey }
  /** Photos the registry holds but cannot read, by arquebusier id, in print order. */
  | { kind: 'photoUnreadable'; ids: string[] }
  /** Selected arquebusiers no longer in the registry. */
  | { kind: 'unknownIds'; ids: string[] };

const CODES: Readonly<Record<string, BadgeErrorKey>> = {
  'badges.notFound': 'notFound',
  'badges.nothingToPrint': 'nothingToPrint',
  'badges.tooMany': 'tooMany',
  'badges.busy': 'busy',
  'badges.auditUnavailable': 'auditUnavailable',
  'storage.unavailable': 'storageUnavailable',
};

const SELECTED_ID = /^arquebusierIds\[(\d+)\]$/;

/**
 * Reads a refused download: the problem code, the unreadable photos' ids, or the selected ids the
 * registry no longer has (`arquebusierIds[i]` → `selection[i]`).
 */
export function badgeFailure(error: unknown, selection: readonly string[]): BadgeFailure {
  if (!(error instanceof ApiProblemError)) return { kind: 'reason', key: 'failed' };
  const problem = error.problem as (Record<string, unknown> & { code?: unknown }) | undefined;
  const code = typeof problem?.code === 'string' ? problem.code : undefined;

  if (code === 'badges.photoUnreadable' && Array.isArray(problem?.arquebusierIds)) {
    return {
      kind: 'photoUnreadable',
      ids: problem.arquebusierIds.filter((id): id is string => typeof id === 'string'),
    };
  }

  const errors = problem?.errors;
  if (code === 'validation' && errors !== null && typeof errors === 'object' && !Array.isArray(errors)) {
    const ids = Object.keys(errors)
      .map((field) => SELECTED_ID.exec(field)?.[1])
      .map((index) => (index === undefined ? undefined : selection[Number(index)]))
      .filter((id): id is string => id !== undefined);
    if (ids.length > 0) return { kind: 'unknownIds', ids };
  }

  const key = code !== undefined && Object.hasOwn(CODES, code) ? CODES[code] : undefined;
  if (key !== undefined) return { kind: 'reason', key };
  if (error.status === 429) return { kind: 'reason', key: 'tooManyRequests' };
  return { kind: 'reason', key: 'failed' };
}
