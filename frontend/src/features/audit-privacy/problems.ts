import type { TFunction } from 'i18next';
import { ApiProblemError } from '@/api/http';

/** The audit log's refusals (spec: Audit log query (UC-25)), as a message in `audit:errors`. */
export function auditProblemMessage(t: TFunction<'audit'>, error: unknown): string {
  if (error instanceof ApiProblemError) {
    if (error.problem?.code === 'validation') return t('errors.validation');
    if (error.status === 429) return t('errors.tooManyRequests');
  }
  return t('errors.generic');
}
