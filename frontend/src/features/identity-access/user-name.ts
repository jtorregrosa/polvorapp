import type { TFunction } from 'i18next';
import type { UserResponse } from '@/api/generated/model';

/** The user's name, or "Erased user" once their data was erased on a GDPR request (add-audit-privacy, design D12). */
export function userName(t: TFunction<'identity'>, user: Pick<UserResponse, 'name' | 'status'>): string {
  return user.status === 'ERASED' ? t('users.erased') : user.name;
}

/** The user's email, or nothing once erased: the API then holds a placeholder address, not theirs. */
export function userEmail(user: Pick<UserResponse, 'email' | 'status'>): string {
  return user.status === 'ERASED' ? '' : user.email;
}
