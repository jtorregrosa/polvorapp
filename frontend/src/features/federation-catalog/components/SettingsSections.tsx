import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { FederationSettingsResponse } from '@/api/generated/model';
import {
  useUpdateCalendarSettings,
  useUpdateEmailSettings,
  useUpdateIdentitySettings,
  useUpdateOrderSettings,
} from '@/api/generated/settings/settings';
import { AlertBanner } from '@/components/app/AlertBanner';
import { DescriptionList } from '@/components/app/DescriptionList';
import { EditSheet } from '@/components/app/EditSheet';
import { FormField } from '@/components/app/FormField';
import { SectionCard } from '@/components/app/SectionCard';
import { SelectInput } from '@/components/app/SelectInput';
import { TextInput } from '@/components/app/TextInput';
import { useAppForm } from '@/components/app/use-app-form';
import {
  calendarSchema,
  CLOSE_REMINDER_DAYS,
  emailsSchema,
  identitySchema,
  MILESTONE_DAYS,
  ordersSchema,
  SETTINGS_LIMITS,
  type CalendarInput,
  type CalendarValues,
  type EmailsInput,
  type EmailsValues,
  type IdentityInput,
  type IdentityValues,
  type OrdersInput,
  type OrdersValues,
} from '../pages/settingsSchema';
import { useSaveSettings } from './useSaveSettings';

const IDENTITY_FIELDS = ['officialNameEs', 'officialNameCa', 'shortName', 'contactEmail', 'website'] as const;
const EMAIL_FIELDS = ['senderName', 'replyTo'] as const;

interface SectionProps {
  settings: FederationSettingsResponse;
}

/** Names and public contact (spec: Federation settings, Identity). */
export function IdentitySection({ settings }: SectionProps) {
  const { t } = useTranslation('catalog');
  const { mutateAsync } = useUpdateIdentitySettings();
  const save = useSaveSettings<IdentityValues, IdentityInput>(settings.version, (data) =>
    mutateAsync({ data }),
  );
  const { identity } = settings;
  const values = useMemo<IdentityValues>(
    () => ({
      officialNameEs: identity.officialNameEs,
      officialNameCa: identity.officialNameCa,
      shortName: identity.shortName,
      contactEmail: identity.contactEmail ?? '',
      website: identity.website ?? '',
    }),
    [identity],
  );
  const form = useAppForm<IdentityValues, unknown, IdentityInput>({
    resolver: zodResolver(identitySchema),
    defaultValues: values,
  });

  return (
    <SectionCard
      title={t('settings.identity.title')}
      description={t('settings.identity.usedIn')}
      action={
        <EditSheet
          title={t('settings.identity.edit')}
          sectionName={t('settings.identity.name')}
          form={form}
          values={values}
          onSave={(submitted) => save(submitted, IDENTITY_FIELDS, form.setError)}
        >
          <AlertBanner severity="info" live={false}>
            {t('settings.identity.federationOnly')}
          </AlertBanner>
          <FormField
            control={form.control}
            name="officialNameEs"
            label={t('settings.identity.officialNameEs')}
          >
            {(field) => <TextInput autoComplete="off" maxLength={SETTINGS_LIMITS.officialName} {...field} />}
          </FormField>
          <FormField
            control={form.control}
            name="officialNameCa"
            label={t('settings.identity.officialNameCa')}
          >
            {(field) => (
              <TextInput autoComplete="off" lang="ca" maxLength={SETTINGS_LIMITS.officialName} {...field} />
            )}
          </FormField>
          <FormField
            control={form.control}
            name="shortName"
            label={t('settings.identity.shortName')}
            description={t('settings.identity.shortNameHint')}
            width="name"
          >
            {(field) => <TextInput autoComplete="off" maxLength={SETTINGS_LIMITS.shortName} {...field} />}
          </FormField>
          <FormField
            control={form.control}
            name="contactEmail"
            label={t('settings.identity.contactEmail')}
            optional
            width="name"
          >
            {(field) => <TextInput type="email" autoComplete="off" spellCheck={false} {...field} />}
          </FormField>
          <FormField
            control={form.control}
            name="website"
            label={t('settings.identity.website')}
            description={t('settings.identity.websiteHint')}
            optional
          >
            {(field) => <TextInput type="url" autoComplete="off" spellCheck={false} {...field} />}
          </FormField>
        </EditSheet>
      }
    >
      <DescriptionList
        items={[
          { term: t('settings.identity.officialNameEs'), value: identity.officialNameEs },
          {
            term: t('settings.identity.officialNameCa'),
            value: <span lang="ca">{identity.officialNameCa}</span>,
          },
          { term: t('settings.identity.shortName'), value: identity.shortName },
          { term: t('settings.identity.contactEmail'), value: identity.contactEmail },
          { term: t('settings.identity.website'), value: identity.website },
        ]}
      />
    </SectionCard>
  );
}

/** The sender name and reply-to; the address itself is a deployment setting (spec: Federation settings, Emails). */
export function EmailsSection({ settings }: SectionProps) {
  const { t } = useTranslation('catalog');
  const { mutateAsync } = useUpdateEmailSettings();
  const save = useSaveSettings<EmailsValues, EmailsInput>(settings.version, (data) => mutateAsync({ data }));
  const { emails } = settings;
  const values = useMemo<EmailsValues>(
    () => ({ senderName: emails.senderName, replyTo: emails.replyTo ?? '' }),
    [emails],
  );
  const form = useAppForm<EmailsValues, unknown, EmailsInput>({
    resolver: zodResolver(emailsSchema),
    defaultValues: values,
  });

  return (
    <SectionCard
      title={t('settings.emails.title')}
      description={t('settings.emails.usedIn')}
      action={
        <EditSheet
          title={t('settings.emails.edit')}
          sectionName={t('settings.emails.name')}
          form={form}
          values={values}
          onSave={(submitted) => save(submitted, EMAIL_FIELDS, form.setError)}
        >
          <FormField
            control={form.control}
            name="senderName"
            label={t('settings.emails.senderName')}
            description={t('settings.emails.senderNameHint', { address: emails.senderAddress })}
            width="name"
          >
            {(field) => <TextInput autoComplete="off" maxLength={SETTINGS_LIMITS.senderName} {...field} />}
          </FormField>
          <FormField
            control={form.control}
            name="replyTo"
            label={t('settings.emails.replyTo')}
            description={t('settings.emails.replyToHint')}
            optional
            width="name"
          >
            {(field) => <TextInput type="email" autoComplete="off" spellCheck={false} {...field} />}
          </FormField>
        </EditSheet>
      }
    >
      <DescriptionList
        items={[
          {
            term: t('settings.emails.sender'),
            value: t('settings.emails.senderLine', {
              name: emails.senderName,
              address: emails.senderAddress,
            }),
          },
          { term: t('settings.emails.replyTo'), value: emails.replyTo },
        ]}
      />
    </SectionCard>
  );
}

/** A lead time as a choice of days, `{{count}} days`. */
function useDayOptions(days: readonly number[]) {
  const { t } = useTranslation('catalog');
  return useMemo(
    () => days.map((count) => ({ value: String(count), label: t('settings.days', { count }) })),
    [days, t],
  );
}

/** The first close reminder lead time (spec: Planned close reminders (BR-10)). */
export function OrdersSection({ settings }: SectionProps) {
  const { t } = useTranslation('catalog');
  const { mutateAsync } = useUpdateOrderSettings();
  const save = useSaveSettings<OrdersValues, OrdersInput>(settings.version, (data) => mutateAsync({ data }));
  const days = settings.orders.closeReminderLeadDays;
  const options = useDayOptions(CLOSE_REMINDER_DAYS);
  const values = useMemo<OrdersValues>(() => ({ closeReminderLeadDays: String(days) }), [days]);
  const form = useAppForm<OrdersValues, unknown, OrdersInput>({
    resolver: zodResolver(ordersSchema),
    defaultValues: values,
  });

  return (
    <SectionCard
      title={t('settings.orders.title')}
      description={t('settings.orders.usedIn')}
      action={
        <EditSheet
          title={t('settings.orders.edit')}
          sectionName={t('settings.orders.name')}
          form={form}
          values={values}
          onSave={(submitted) => save(submitted, ['closeReminderLeadDays'], form.setError)}
        >
          <FormField
            control={form.control}
            name="closeReminderLeadDays"
            label={t('settings.orders.closeReminderLeadDays')}
            description={t('settings.orders.closeReminderHint')}
            width="short"
          >
            {(field) => <SelectInput options={options} {...field} />}
          </FormField>
        </EditSheet>
      }
    >
      <DescriptionList
        items={[
          {
            term: t('settings.orders.closeReminderLeadDays'),
            value: t('settings.daysBefore', { count: days }),
          },
        ]}
      />
    </SectionCard>
  );
}

/** The milestone reminder lead time (spec: Milestone reminders). */
export function CalendarSection({ settings }: SectionProps) {
  const { t } = useTranslation('catalog');
  const { mutateAsync } = useUpdateCalendarSettings();
  const save = useSaveSettings<CalendarValues, CalendarInput>(settings.version, (data) =>
    mutateAsync({ data }),
  );
  const days = settings.calendar.milestoneLeadDays;
  const options = useDayOptions(MILESTONE_DAYS);
  const values = useMemo<CalendarValues>(() => ({ milestoneLeadDays: String(days) }), [days]);
  const form = useAppForm<CalendarValues, unknown, CalendarInput>({
    resolver: zodResolver(calendarSchema),
    defaultValues: values,
  });

  return (
    <SectionCard
      title={t('settings.calendar.title')}
      description={t('settings.calendar.usedIn')}
      action={
        <EditSheet
          title={t('settings.calendar.edit')}
          sectionName={t('settings.calendar.name')}
          form={form}
          values={values}
          onSave={(submitted) => save(submitted, ['milestoneLeadDays'], form.setError)}
        >
          <FormField
            control={form.control}
            name="milestoneLeadDays"
            label={t('settings.calendar.milestoneLeadDays')}
            width="short"
          >
            {(field) => <SelectInput options={options} {...field} />}
          </FormField>
        </EditSheet>
      }
    >
      <DescriptionList
        items={[
          {
            term: t('settings.calendar.milestoneLeadDays'),
            value: t('settings.daysBefore', { count: days }),
          },
        ]}
      />
    </SectionCard>
  );
}
