import { useRef, useState, type ReactNode } from 'react';
import { useController, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ArquebusierStatus, Gender, LicenseType, type ComparsaResponse } from '@/api/generated/model';
import { CheckboxField } from '@/components/app/CheckboxField';
import { DateInput } from '@/components/app/DateInput';
import { FormField } from '@/components/app/FormField';
import { RadioCards } from '@/components/app/RadioCards';
import { SelectInput } from '@/components/app/SelectInput';
import { TextInput } from '@/components/app/TextInput';
import { todayIso } from '@/lib/dates';
import { useFormatters } from '@/lib/format';
import {
  defaultExpiry,
  MAX_EMAIL_LENGTH,
  MAX_NAME_LENGTH,
  MAX_PHONE_LENGTH,
  type ArquebusierValues,
} from '../arquebusierSchema';

type ArquebusierForm = UseFormReturn<ArquebusierValues>;

interface SectionProps {
  form: ArquebusierForm;
}

/** RadioCards cannot hold an empty value: "no license" is this option. */
const NO_LICENSE = 'NONE';

/**
 * The comparsa and the status of a new arquebusier (spec: Registry screens): the comparsa among the
 * active ones in scope, and the status as radio cards. On the detail page a transfer and "More
 * actions" change them instead.
 */
export function RegisterFields({
  form,
  comparsas,
}: SectionProps & { comparsas: readonly ComparsaResponse[] }) {
  const { t } = useTranslation('registry');
  const { control } = form;
  return (
    <>
      <FormField control={control} name="comparsaId" label={t('form.comparsa')} width="name">
        {(field) => (
          <SelectInput
            {...field}
            placeholder={t('validation.choice')}
            options={comparsas.map((comparsa) => ({ value: comparsa.id, label: comparsa.name }))}
          />
        )}
      </FormField>
      <FormField control={control} name="status" label={t('form.status')} description={t('form.statusHint')}>
        {(field) => (
          <RadioCards
            {...field}
            options={Object.values(ArquebusierStatus).map((value) => ({
              value,
              label: t(`status.arquebusier.${value}`, { ns: 'ui' }),
            }))}
          />
        )}
      </FormField>
    </>
  );
}

/** Personal data (spec: Arquebusier data): identifiers, name, birth date, gender and contact. */
export function PersonalFields({ form, idPhoto }: SectionProps & { idPhoto?: ReactNode }) {
  const { t } = useTranslation('registry');
  const { control } = form;
  const today = todayIso();
  return (
    <>
      <FormField
        control={control}
        name="federationId"
        label={t('form.federationId')}
        description={t('form.federationIdHint')}
        width="id"
      >
        {(field) => <TextInput autoComplete="off" inputMode="numeric" maxLength={9} {...field} />}
      </FormField>
      <FormField
        control={control}
        name="nationalId"
        label={t('form.nationalId')}
        description={t('form.nationalIdHint')}
        width="id"
      >
        {(field) => (
          <TextInput
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            maxLength={64}
            className="font-mono"
            {...field}
          />
        )}
      </FormField>
      <FormField control={control} name="firstName" label={t('form.firstName')} width="name">
        {(field) => <TextInput autoComplete="off" maxLength={MAX_NAME_LENGTH} {...field} />}
      </FormField>
      <FormField control={control} name="lastName" label={t('form.lastName')} width="name">
        {(field) => <TextInput autoComplete="off" maxLength={MAX_NAME_LENGTH} {...field} />}
      </FormField>
      <FormField control={control} name="birthDate" label={t('form.birthDate')} width="short">
        {(field) => <DateInput {...field} min="1900-01-01" max={today} />}
      </FormField>
      <FormField control={control} name="gender" label={t('form.gender')} description={t('form.genderHint')}>
        {(field) => (
          <RadioCards
            {...field}
            options={Object.values(Gender).map((value) => ({ value, label: t(`gender.${value}`) }))}
          />
        )}
      </FormField>
      <FormField control={control} name="email" label={t('form.email')} width="long" optional>
        {(field) => <TextInput type="email" autoComplete="off" maxLength={MAX_EMAIL_LENGTH} {...field} />}
      </FormField>
      <FormField control={control} name="phone" label={t('form.phone')} width="short" optional>
        {(field) => <TextInput type="tel" autoComplete="off" maxLength={MAX_PHONE_LENGTH} {...field} />}
      </FormField>
      {idPhoto}
    </>
  );
}

/**
 * The current license (spec: Current license): the type as radio cards, and under a chosen type
 * whether it is pending and, when it is not, its dates. The expiry is filled in from the issue date
 * and type (BR-03) until the user edits it, and the new date is announced.
 */
export function LicenseFields({ form, licensePhotos }: SectionProps & { licensePhotos?: ReactNode }) {
  const { t } = useTranslation('registry');
  const { control } = form;
  const today = todayIso();
  const pending = useController({ control, name: 'licensePending' });
  const expiry = useLicenseDates(form);

  const details = (
    <>
      <CheckboxField
        label={t('form.pending')}
        description={t('form.pendingHint')}
        checked={pending.field.value}
        onCheckedChange={(checked) => {
          pending.field.onChange(checked);
          if (checked) expiry.clear();
          else expiry.restore();
        }}
      />
      {!pending.field.value && (
        <>
          <FormField control={control} name="issuedOn" label={t('form.issuedOn')} width="short">
            {(field) => (
              <DateInput
                {...field}
                min="1900-01-01"
                max={today}
                onChange={(value) => {
                  field.onChange(value);
                  expiry.recompute(form.getValues('licenseType'), value);
                }}
              />
            )}
          </FormField>
          <FormField
            control={control}
            name="expiresOn"
            label={t('form.expiresOn')}
            description={t('form.expiresOnHint')}
            width="short"
            optional
          >
            {(field) => (
              <DateInput
                {...field}
                onChange={(value) => {
                  expiry.edited(value);
                  field.onChange(value);
                }}
              />
            )}
          </FormField>
        </>
      )}
    </>
  );

  return (
    <>
      <FormField control={control} name="licenseType" label={t('form.licenseType')}>
        {(field) => (
          <RadioCards
            {...field}
            value={field.value === '' ? NO_LICENSE : field.value}
            onChange={(value) => {
              const next = value === NO_LICENSE ? '' : value;
              const hadLicense = field.value !== '';
              field.onChange(next);
              if (next === '') {
                form.setValue('licensePending', false, { shouldDirty: true });
                expiry.clear();
              } else if (!hadLicense) {
                expiry.restore();
              } else {
                expiry.recompute(next, form.getValues('issuedOn'));
              }
            }}
            options={[
              { value: NO_LICENSE, label: t('form.noLicense') },
              ...Object.values(LicenseType).map((value) => ({
                value,
                label: t(`licenseType.${value}`),
                reveal: details,
              })),
            ]}
          />
        )}
      </FormField>
      {/* The computed expiry changes another field: say so to screen-reader users (WCAG 4.1.3). */}
      <p role="status" className="sr-only">
        {expiry.announcement}
      </p>
      {licensePhotos}
    </>
  );
}

/** The training course (spec: Training course): only whether it is done, and when. */
export function CourseFields({ form }: SectionProps) {
  const { t } = useTranslation('registry');
  return (
    <FormField
      control={form.control}
      name="trainingCompletedOn"
      label={t('form.trainingCompletedOn')}
      description={t('form.trainingCompletedOnHint')}
      width="short"
      optional
    >
      {(field) => <DateInput {...field} min="1900-01-01" max={todayIso()} clearable />}
    </FormField>
  );
}

/** Issue dates before this are typing in progress (a year segment of one to three digits). */
const EARLIEST_ISSUE = '1900-01-01';

/**
 * The license dates in the form (BR-03): keeps the expiry at its default (5 years for AE, 1 for
 * A-PROF) while the user has not chosen another date, and announces each new default. Dates cleared
 * because the license became pending or was removed come back when that is undone, so a mis-click
 * loses nothing; both changes are announced.
 */
function useLicenseDates(form: ArquebusierForm) {
  const { t } = useTranslation('registry');
  const formatters = useFormatters();
  const initial = form.getValues();
  // An existing expiry that differs from the default was chosen by someone: keep it.
  const edited = useRef(
    initial.expiresOn !== '' && initial.expiresOn !== defaultExpiry(initial.licenseType, initial.issuedOn),
  );
  const lastDefault = useRef('');
  const cleared = useRef<{ issuedOn: string; expiresOn: string; edited: boolean } | null>(null);
  const [announcement, setAnnouncement] = useState('');

  const recompute = (licenseType: string, issuedOn: string) => {
    if (edited.current || form.getValues('licensePending')) {
      return;
    }
    const current = form.getValues('expiresOn');
    const computed = issuedOn >= EARLIEST_ISSUE ? defaultExpiry(licenseType, issuedOn) : '';
    if (computed === '') {
      // The default no longer follows from the issue date: drop it rather than leave a stale one.
      if (current !== '' && current === lastDefault.current) {
        form.setValue('expiresOn', '', { shouldDirty: true });
      }
      lastDefault.current = '';
      return;
    }
    if (computed !== current) {
      form.setValue('expiresOn', computed, { shouldDirty: true });
      form.clearErrors('expiresOn');
      setAnnouncement(
        t('form.expiresOnComputed', { date: formatters.date(new Date(`${computed}T12:00:00Z`)) }),
      );
    }
    lastDefault.current = computed;
  };

  return {
    announcement,
    recompute,
    /** The user typed an expiry: stop recomputing it, unless they emptied the field. */
    edited: (value: string) => {
      edited.current = value !== '';
    },
    /** The license became pending or was removed: empty the dates, keeping them to restore. */
    clear: () => {
      const { issuedOn, expiresOn } = form.getValues();
      if (issuedOn !== '' || expiresOn !== '') {
        cleared.current = { issuedOn, expiresOn, edited: edited.current };
        setAnnouncement(t('form.datesCleared'));
      }
      edited.current = false;
      lastDefault.current = '';
      form.setValue('issuedOn', '', { shouldDirty: true });
      form.setValue('expiresOn', '', { shouldDirty: true });
      form.clearErrors(['issuedOn', 'expiresOn']);
    },
    /** Dates can be entered again: bring back the ones cleared, if the fields are still empty. */
    restore: () => {
      const saved = cleared.current;
      cleared.current = null;
      const { licenseType, issuedOn, expiresOn } = form.getValues();
      if (!saved || issuedOn !== '' || expiresOn !== '') {
        return;
      }
      form.setValue('issuedOn', saved.issuedOn, { shouldDirty: true });
      form.setValue('expiresOn', saved.expiresOn, { shouldDirty: true });
      edited.current = saved.edited;
      lastDefault.current = saved.edited ? '' : saved.expiresOn;
      setAnnouncement(t('form.datesRestored'));
      // The type may have changed meanwhile (A-PROF instead of AE): follow it unless chosen by hand.
      recompute(licenseType, saved.issuedOn);
    },
  };
}
