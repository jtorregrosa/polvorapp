import { useRef, useState } from 'react';
import { useController, useWatch, type UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ArquebusierStatus, Gender, LicenseType, type ComparsaResponse } from '@/api/generated/model';
import { CheckboxField } from '@/components/app/CheckboxField';
import { DateInput } from '@/components/app/DateInput';
import { FormField } from '@/components/app/FormField';
import { FormSection } from '@/components/app/FormSection';
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

export interface ArquebusierFieldsProps {
  form: ArquebusierForm;
  /** The active comparsas the caller may register in; the comparsa is only chosen when registering. */
  comparsas?: readonly ComparsaResponse[];
}

/**
 * Personal data, license and training course of an arquebusier (spec: Registry screens). The
 * license expiry is filled in from the issue date and type (BR-03) until the user edits it, and the
 * new date is announced; a pending license has no dates.
 */
export function ArquebusierFields({ form, comparsas }: ArquebusierFieldsProps) {
  const { t } = useTranslation('registry');
  const { control } = form;
  const today = todayIso();
  const licenseType = useWatch({ control, name: 'licenseType' });
  const pending = useController({ control, name: 'licensePending' });
  const hasLicense = licenseType !== '';
  const datesDisabled = !hasLicense || pending.field.value;
  const expiry = useLicenseDates(form);

  return (
    <>
      <FormSection title={t('form.personal')} description={t('form.personalDescription')}>
        {comparsas && (
          <FormField control={control} name="comparsaId" label={t('form.comparsa')} required>
            {(field) => (
              <SelectInput
                {...field}
                options={[
                  { value: '', label: t('validation.choice') },
                  ...comparsas.map((comparsa) => ({ value: comparsa.id, label: comparsa.name })),
                ]}
              />
            )}
          </FormField>
        )}
        <FormField
          control={control}
          name="federationId"
          label={t('form.federationId')}
          description={t('form.federationIdHint')}
          required
        >
          {(field) => <TextInput autoComplete="off" inputMode="numeric" maxLength={9} {...field} />}
        </FormField>
        <FormField
          control={control}
          name="nationalId"
          label={t('form.nationalId')}
          description={t('form.nationalIdHint')}
          required
        >
          {(field) => (
            <TextInput
              autoComplete="off"
              autoCapitalize="characters"
              spellCheck={false}
              maxLength={64}
              {...field}
              onBlur={() => {
                field.onBlur();
                // Spec "Registry screens": a wrong check letter shows as soon as the field is left.
                if (field.value !== '') void form.trigger('nationalId');
              }}
            />
          )}
        </FormField>
        <FormField control={control} name="firstName" label={t('form.firstName')} required>
          {(field) => <TextInput autoComplete="off" maxLength={MAX_NAME_LENGTH} {...field} />}
        </FormField>
        <FormField control={control} name="lastName" label={t('form.lastName')} required>
          {(field) => <TextInput autoComplete="off" maxLength={MAX_NAME_LENGTH} {...field} />}
        </FormField>
        <FormField control={control} name="birthDate" label={t('form.birthDate')} required>
          {(field) => <DateInput {...field} min="1900-01-01" max={today} />}
        </FormField>
        <FormField
          control={control}
          name="gender"
          label={t('form.gender')}
          description={t('form.genderHint')}
          required
        >
          {(field) => (
            <SelectInput
              {...field}
              options={[
                { value: '', label: t('validation.choice') },
                ...Object.values(Gender).map((value) => ({ value, label: t(`gender.${value}`) })),
              ]}
            />
          )}
        </FormField>
        <FormField control={control} name="email" label={t('form.email')}>
          {(field) => <TextInput type="email" autoComplete="off" maxLength={MAX_EMAIL_LENGTH} {...field} />}
        </FormField>
        <FormField control={control} name="phone" label={t('form.phone')}>
          {(field) => <TextInput type="tel" autoComplete="off" maxLength={MAX_PHONE_LENGTH} {...field} />}
        </FormField>
        <FormField
          control={control}
          name="status"
          label={t('form.status')}
          description={t('form.statusHint')}
          required
        >
          {(field) => (
            <SelectInput
              {...field}
              options={Object.values(ArquebusierStatus).map((value) => ({
                value,
                label: t(`status.arquebusier.${value}`, { ns: 'ui' }),
              }))}
            />
          )}
        </FormField>
      </FormSection>

      <FormSection title={t('form.license')} description={t('form.licenseDescription')}>
        <FormField control={control} name="licenseType" label={t('form.licenseType')}>
          {(field) => (
            <SelectInput
              {...field}
              onChange={(event) => {
                const hadLicense = field.value !== '';
                field.onChange(event);
                if (event.target.value === '') {
                  form.setValue('licensePending', false, { shouldDirty: true });
                  expiry.clear();
                } else if (!hadLicense) {
                  expiry.restore();
                } else {
                  expiry.recompute(event.target.value, form.getValues('issuedOn'));
                }
              }}
              options={[
                { value: '', label: t('form.noLicense') },
                ...Object.values(LicenseType).map((value) => ({ value, label: t(`licenseType.${value}`) })),
              ]}
            />
          )}
        </FormField>
        <CheckboxField
          label={t('form.pending')}
          description={t('form.pendingHint')}
          checked={pending.field.value}
          disabled={!hasLicense}
          onCheckedChange={(checked) => {
            pending.field.onChange(checked);
            if (checked) expiry.clear();
            else expiry.restore();
          }}
        />
        <FormField
          control={control}
          name="issuedOn"
          label={t('form.issuedOn')}
          description={hasLicense ? undefined : t('form.datesNeedType')}
          required={!datesDisabled}
        >
          {(field) => (
            <DateInput
              {...field}
              min="1900-01-01"
              max={today}
              disabled={datesDisabled}
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
        >
          {(field) => (
            <DateInput
              {...field}
              disabled={datesDisabled}
              onChange={(value) => {
                expiry.edited(value);
                field.onChange(value);
              }}
            />
          )}
        </FormField>
        {/* The computed expiry changes another field: say so to screen-reader users (WCAG 4.1.3). */}
        <p role="status" className="sr-only">
          {expiry.announcement}
        </p>
      </FormSection>

      <FormSection title={t('form.training')} description={t('form.trainingDescription')}>
        <FormField
          control={control}
          name="trainingCompletedOn"
          label={t('form.trainingCompletedOn')}
          description={t('form.trainingCompletedOnHint')}
        >
          {(field) => <DateInput {...field} min="1900-01-01" max={today} clearable />}
        </FormField>
      </FormSection>
    </>
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
