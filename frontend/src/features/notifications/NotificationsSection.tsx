import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useRef } from 'react';
import { useController, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import {
  getGetNotificationPreferencesQueryKey,
  useGetNotificationPreferences,
  useSaveNotificationPreferences,
} from '@/api/generated/notifications/notifications';
import type { NotificationKind } from '@/api/generated/model';
import { ApiProblemError, responseData } from '@/api/http';
import { CheckboxField } from '@/components/app/CheckboxField';
import { DescriptionList } from '@/components/app/DescriptionList';
import { EditSheet, type EditResult } from '@/components/app/EditSheet';
import { SectionCard } from '@/components/app/SectionCard';
import { useAppForm } from '@/components/app/use-app-form';
import { LoadFailure } from '@/features/arquebusier-registry/components/LoadFailure';
import { problemMessage } from '@/features/identity-access/problems';

/** The section's anchor; email footers link to `/account?section=notifications`. */
export const NOTIFICATIONS_SECTION = 'notifications';

/** The reasons the API gives for a refused kind (`kinds[i].kind`), translated in `notifications:errors`. */
const KIND_ERRORS = ['invalid', 'notApplicable', 'duplicate'] as const;
type KindError = (typeof KIND_ERRORS)[number];

type PreferenceValues = Partial<Record<NotificationKind, boolean>>;

function KindCheckbox({ form, kind }: { form: UseFormReturn<PreferenceValues>; kind: NotificationKind }) {
  const { t } = useTranslation('notifications');
  const { field } = useController({ control: form.control, name: kind });
  return (
    <CheckboxField
      label={t(`kinds.${kind}.label`)}
      description={t(`kinds.${kind}.description`)}
      checked={field.value === true}
      onCheckedChange={field.onChange}
    />
  );
}

/** The first refused kind's reason, when the API refused the kinds themselves (e.g. a role changed meanwhile). */
function kindError(error: unknown): KindError | undefined {
  if (!(error instanceof ApiProblemError)) return undefined;
  const errors = (error.problem as { errors?: Record<string, string> } | undefined)?.errors ?? {};
  return Object.values(errors).find((reason): reason is KindError =>
    (KIND_ERRORS as readonly string[]).includes(reason),
  );
}

/**
 * Spec "Notification screens": the kinds of email of the user's role, each with what it covers and
 * whether it is on, edited in a side panel with one checkbox per kind. Its own failure never hides the
 * rest of the account page, and a failed refresh keeps what was shown (and an open panel).
 */
export function NotificationsSection() {
  const { t } = useTranslation('notifications');
  const { t: tIdentity } = useTranslation('identity');
  const queryClient = useQueryClient();
  // An empty body is a failed request, never shown as "no notifications".
  const preferences = useGetNotificationPreferences({ query: { select: responseData } });
  const save = useSaveNotificationPreferences();
  const [searchParams] = useSearchParams();
  const sectionRef = useRef<HTMLElement>(null);
  const linked = searchParams.get('section') === NOTIFICATIONS_SECTION;
  const loaded = preferences.data;
  const kinds = useMemo(() => loaded?.kinds ?? [], [loaded]);
  const values = useMemo<PreferenceValues>(
    () => Object.fromEntries(kinds.map((preference) => [preference.kind, preference.enabled])),
    [kinds],
  );
  const form = useAppForm<PreferenceValues>({ defaultValues: values });
  const settled = !preferences.isPending;

  useEffect(() => {
    // From an email's settings link: bring the section into view and move focus to it (WCAG 2.4.3),
    // once there is something to read there, the preferences or why they are missing.
    if (linked && settled) {
      sectionRef.current?.scrollIntoView({ block: 'start' });
      sectionRef.current?.focus();
    }
  }, [linked, settled]);

  const onSave = async (submitted: PreferenceValues): Promise<EditResult> => {
    const queryKey = getGetNotificationPreferencesQueryKey();
    try {
      const response = await save.mutateAsync({
        data: { kinds: kinds.map(({ kind }) => ({ kind, enabled: submitted[kind] === true })) },
      });
      // An empty answer is a failed save, never shown as preferences.
      responseData(response);
      await queryClient.cancelQueries({ queryKey });
      queryClient.setQueryData(queryKey, response);
    } catch (error) {
      const reason = kindError(error);
      if (reason) {
        // The kinds the page shows are out of date (e.g. the role changed): show the current ones.
        void queryClient.invalidateQueries({ queryKey });
        return { status: 'rejected', reason: t(`errors.${reason}`) };
      }
      return { status: 'rejected', reason: problemMessage(tIdentity, error) };
    }
    return { status: 'saved' };
  };

  return (
    <SectionCard
      title={t('section.title')}
      description={t('section.description')}
      anchorId={NOTIFICATIONS_SECTION}
      ref={sectionRef}
      action={
        kinds.length > 0 && (
          <EditSheet
            title={t('section.editTitle')}
            description={t('section.editDescription')}
            sectionName={t('section.edit')}
            form={form}
            values={values}
            onSave={onSave}
          >
            {kinds.map(({ kind }) => (
              <KindCheckbox key={kind} form={form} kind={kind} />
            ))}
          </EditSheet>
        )
      }
    >
      {preferences.isPending && (
        <p role="status" className="text-help text-muted-foreground">
          {t('section.loading')}
        </p>
      )}
      {preferences.isError && (
        <LoadFailure
          error={preferences.error}
          consequence={t('section.loadFailed')}
          onRetry={() => preferences.refetch()}
        />
      )}
      {kinds.length > 0 && (
        <DescriptionList
          items={kinds.map(({ kind, enabled }) => ({
            term: t(`kinds.${kind}.label`),
            value: (
              <span className="flex flex-col gap-0.5">
                <span className="font-semibold">{enabled ? t('state.on') : t('state.off')}</span>
                <span className="text-help text-muted-foreground">{t(`kinds.${kind}.description`)}</span>
              </span>
            ),
          }))}
        />
      )}
    </SectionCard>
  );
}
