import { useTranslation } from 'react-i18next';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { getGetFederationQueryKey } from '@/api/generated/federation/federation';
import { getGetFederationSettingsQueryKey } from '@/api/generated/settings/settings';
import { ApiProblemError } from '@/api/http';
import type { EditResult } from '@/components/app/EditSheet';
import { useInvalidate } from '@/lib/use-invalidate';
import { messages, problemCode } from '../problems';
import { SETTINGS_LIMITS, settingsMessages } from '../pages/settingsSchema';

/** Refreshes the settings and the identity every user reads after any change, the logo included. */
export function useSettingsRefresh(): () => Promise<void> {
  return useInvalidate([getGetFederationSettingsQueryKey(), getGetFederationQueryKey()]);
}

/** The limit the API names with `tooLong` for each field. */
const LIMITS: Record<string, number> = {
  officialNameEs: SETTINGS_LIMITS.officialName,
  officialNameCa: SETTINGS_LIMITS.officialName,
  shortName: SETTINGS_LIMITS.shortName,
  senderName: SETTINGS_LIMITS.senderName,
  contactEmail: SETTINGS_LIMITS.email,
  replyTo: SETTINGS_LIMITS.email,
  website: SETTINGS_LIMITS.website,
};

/** The message for a field reason of the API (spec: Federation settings), as `FormField` translates it. */
function fieldMessage(field: string, reason: string): string {
  switch (reason) {
    case 'required':
      return messages.required;
    case 'tooLong':
      return LIMITS[field] ? settingsMessages.tooLong(LIMITS[field]) : messages.invalid;
    case 'outOfRange':
      return settingsMessages.outOfRange;
    default:
      if (field === 'contactEmail' || field === 'replyTo') return settingsMessages.email;
      if (field === 'website') return settingsMessages.website;
      if (field === 'senderName') return settingsMessages.senderName;
      return messages.invalid;
  }
}

/** A translated reason for a failed settings request. */
export function useSettingsProblem(): (error: unknown) => string {
  const { t } = useTranslation('catalog');
  return (error) => {
    if (error instanceof ApiProblemError && error.status === 429) return t('errors.tooManyRequests');
    switch (problemCode(error)) {
      case 'federationSettings.modified':
        return t('settings.errors.modified');
      case 'federationSettings.busy':
        return t('settings.errors.busy');
      case 'validation':
        return t('errors.validation');
      default:
        return t('errors.generic');
    }
  };
}

/**
 * Saves one settings section against the version the page shows (spec: Settings screen; design D2).
 * A newer version reloads the settings and keeps the panel open with the reason; field errors land
 * on the section's fields. Any save also refreshes the identity every user reads.
 */
export function useSaveSettings<TValues extends FieldValues, TBody>(
  version: number,
  mutate: (body: TBody & { version: number }) => Promise<unknown>,
) {
  const refresh = useSettingsRefresh();
  const explain = useSettingsProblem();

  return async (
    body: TBody,
    fields: readonly Path<TValues>[],
    setError: UseFormSetError<TValues>,
  ): Promise<EditResult> => {
    try {
      await mutate({ ...body, version });
    } catch (error) {
      if (problemCode(error) === 'federationSettings.modified') {
        await refresh();
        return { status: 'conflict', reason: explain(error) };
      }
      const errors =
        error instanceof ApiProblemError && problemCode(error) === 'validation'
          ? error.problem?.errors
          : undefined;
      let applied = false;
      if (errors && !Array.isArray(errors)) {
        for (const field of fields) {
          const reason = errors[field];
          if (reason) {
            setError(field, { type: 'server', message: fieldMessage(field, reason) });
            applied = true;
          }
        }
      }
      return applied ? { status: 'kept' } : { status: 'rejected', reason: explain(error) };
    }
    await refresh();
    return { status: 'saved' };
  };
}
