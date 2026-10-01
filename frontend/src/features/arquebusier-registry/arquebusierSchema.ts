import { z } from 'zod';
import {
  ArquebusierStatus,
  Gender,
  LicenseType,
  type ArquebusierResponse,
  type LicenseRequest,
} from '@/api/generated/model';
import { INCOMPLETE_DATE } from '@/components/app/DateInput';
import { isIsoDate, todayIso } from '@/lib/dates';
import { parseNationalId } from './nationalId';
import { messages } from './problems';

/** Limits of the API (Arquebusier.NameMaxLength and friends, design D3). */
export const MAX_NAME_LENGTH = 100;
export const MAX_EMAIL_LENGTH = 254;
export const MAX_PHONE_LENGTH = 20;
export const MAX_FEDERATION_ID = 999_999_999;
const EARLIEST_BIRTH_DATE = '1900-01-01';
const PHONE = /^\+?[0-9 ]+$/;
/** The API takes printable ASCII only in emails (InputFields.IsEmail). */
const ASCII_ONLY = /^[\x21-\x7e]+$/;
/** What the API refuses in names (InputFields): control, format, private-use, unassigned, separators. */
const UNPRINTABLE = /[\p{Cc}\p{Cf}\p{Co}\p{Cn}\p{Zl}\p{Zp}]/u;
const EMAIL = /^[^\s@"(),:;<>[\]\\]+@[^\s@"(),:;<>[\]\\]+\.[^\s@"(),:;<>[\]\\]+$/;

const GENDERS: readonly string[] = Object.values(Gender);
const STATUSES: readonly string[] = Object.values(ArquebusierStatus);
const LICENSE_TYPES: readonly string[] = Object.values(LicenseType);

/**
 * BR-03: AE licenses last 5 years and A-PROF ones 1 year; 29 February maps to 28 February, as the
 * API does. Returns `''` when the issue date is not a complete date yet.
 */
export function defaultExpiry(type: string, issuedOn: string): string {
  if (!isIsoDate(issuedOn) || !LICENSE_TYPES.includes(type)) {
    return '';
  }
  const [year, month, day] = issuedOn.split('-').map(Number) as [number, number, number];
  const target = year + (type === LicenseType.AE ? 5 : 1);
  const lastDay = new Date(Date.UTC(target, month, 0)).getUTCDate();
  return `${String(target).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(Math.min(day, lastDay)).padStart(2, '0')}`;
}

/**
 * The arquebusier form as the register and edit pages validate it (specs: Arquebusier data,
 * National ID validation, Current license, Training course), mirroring the server's blocking rules
 * so most mistakes are caught before submitting. Every rule is checked in one pass.
 */
export const arquebusierSchema = z
  .object({
    comparsaId: z.string(),
    federationId: z.string(),
    nationalId: z.string(),
    firstName: z.string(),
    lastName: z.string(),
    birthDate: z.string(),
    email: z.string(),
    phone: z.string(),
    gender: z.string(),
    status: z.string(),
    trainingCompletedOn: z.string(),
    licenseType: z.string(),
    licensePending: z.boolean(),
    issuedOn: z.string(),
    expiresOn: z.string(),
  })
  .superRefine((values, context) => {
    const issue = (path: string, message: string) => {
      context.addIssue({ code: 'custom', path: [path], message });
    };
    const today = todayIso();

    const federationId = values.federationId.trim();
    if (federationId === '') {
      issue('federationId', messages.required);
    } else if (
      !/^\d{1,9}$/.test(federationId) ||
      Number(federationId) < 1 ||
      Number(federationId) > MAX_FEDERATION_ID
    ) {
      issue('federationId', messages.federationId);
    }

    const nationalId = parseNationalId(values.nationalId);
    if ('error' in nationalId) {
      issue(
        'nationalId',
        nationalId.error === 'invalid' ? messages.nationalIdInvalid : messages[nationalId.error],
      );
    }

    for (const name of ['firstName', 'lastName'] as const) {
      const text = values[name].trim();
      if (text === '') {
        issue(name, messages.required);
      } else if (text.length > MAX_NAME_LENGTH) {
        issue(name, messages.tooLong);
      } else if (UNPRINTABLE.test(text)) {
        issue(name, messages.invalid);
      }
    }

    checkDate(values.birthDate, 'birthDate', { required: true, today, earliest: EARLIEST_BIRTH_DATE }, issue);
    checkDate(values.trainingCompletedOn, 'trainingCompletedOn', { required: false, today }, issue);

    const email = values.email.trim();
    if (email.length > MAX_EMAIL_LENGTH) {
      issue('email', messages.tooLong);
    } else if (email !== '' && (!EMAIL.test(email) || !ASCII_ONLY.test(email))) {
      issue('email', messages.email);
    }

    const phone = values.phone.trim().replace(/ +/g, ' ');
    if (phone.length > MAX_PHONE_LENGTH) {
      issue('phone', messages.tooLong);
    } else if (phone !== '' && (!PHONE.test(phone) || phone === '+')) {
      issue('phone', messages.phone);
    }

    if (!GENDERS.includes(values.gender)) {
      issue('gender', messages.choice);
    }
    if (!STATUSES.includes(values.status)) {
      issue('status', messages.choice);
    }

    if (values.licenseType !== '' && !values.licensePending) {
      checkDate(values.issuedOn, 'issuedOn', { required: true, today }, issue);
      checkDate(values.expiresOn, 'expiresOn', { required: false }, issue);
      if (isIsoDate(values.issuedOn) && isIsoDate(values.expiresOn) && values.expiresOn <= values.issuedOn) {
        issue('expiresOn', messages.notAfterIssued);
      }
    }
  });

function checkDate(
  value: string,
  path: string,
  { required, today, earliest }: { required: boolean; today?: string; earliest?: string },
  issue: (path: string, message: string) => void,
): void {
  if (value === '') {
    if (required) issue(path, messages.required);
  } else if (value === INCOMPLETE_DATE || !isIsoDate(value)) {
    issue(path, messages.date);
  } else if (today && value > today) {
    issue(path, messages.future);
  } else if (earliest && value < earliest) {
    issue(path, messages.tooOld);
  }
}

/** What the form holds: strings throughout, `''` for an empty value (never `null`). */
export type ArquebusierValues = z.input<typeof arquebusierSchema>;

export const EMPTY_ARQUEBUSIER: ArquebusierValues = {
  comparsaId: '',
  federationId: '',
  nationalId: '',
  firstName: '',
  lastName: '',
  birthDate: '',
  email: '',
  phone: '',
  gender: '',
  status: ArquebusierStatus.ACTIVE,
  trainingCompletedOn: '',
  licenseType: '',
  licensePending: false,
  issuedOn: '',
  expiresOn: '',
};

/** The form values of an existing arquebusier: `null` becomes `''`. */
export function valuesOf(arquebusier: ArquebusierResponse): ArquebusierValues {
  return {
    comparsaId: arquebusier.comparsaId,
    federationId: String(arquebusier.federationId),
    nationalId: arquebusier.nationalId,
    firstName: arquebusier.firstName,
    lastName: arquebusier.lastName,
    birthDate: arquebusier.birthDate,
    email: arquebusier.email ?? '',
    phone: arquebusier.phone ?? '',
    gender: arquebusier.gender,
    status: arquebusier.status,
    trainingCompletedOn: arquebusier.trainingCompletedOn ?? '',
    licenseType: arquebusier.license?.type ?? '',
    licensePending: arquebusier.license?.pending ?? false,
    issuedOn: arquebusier.license?.issuedOn ?? '',
    expiresOn: arquebusier.license?.expiresOn ?? '',
  };
}

/** The register form: the edit form plus the comparsa, which is chosen only when registering. */
export const registerSchema = arquebusierSchema.superRefine((values, context) => {
  if (values.comparsaId === '') {
    context.addIssue({ code: 'custom', path: ['comparsaId'], message: messages.choice });
  }
});

/** The editable fields as the API takes them: `''` becomes `null`; the license only when it has a type. */
export function requestFields(values: ArquebusierValues) {
  const optional = (value: string) => (value.trim() === '' ? null : value.trim());
  const license: LicenseRequest | null =
    values.licenseType === ''
      ? null
      : {
          type: values.licenseType,
          pending: values.licensePending,
          issuedOn: values.licensePending ? null : optional(values.issuedOn),
          expiresOn: values.licensePending ? null : optional(values.expiresOn),
        };
  return {
    federationId: Number(values.federationId.trim()),
    nationalId: values.nationalId,
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    birthDate: values.birthDate,
    email: optional(values.email),
    phone: optional(values.phone),
    gender: values.gender,
    status: values.status,
    trainingCompletedOn: optional(values.trainingCompletedOn),
    license,
  };
}

/** API field names (validation errors) mapped to the form's fields. */
export const ARQUEBUSIER_FIELDS = {
  comparsaId: 'comparsaId',
  federationId: 'federationId',
  nationalId: 'nationalId',
  firstName: 'firstName',
  lastName: 'lastName',
  birthDate: 'birthDate',
  email: 'email',
  phone: 'phone',
  gender: 'gender',
  status: 'status',
  trainingCompletedOn: 'trainingCompletedOn',
  'license.type': 'licenseType',
  // The pending box is not a FormField: its errors are shown on the license type.
  'license.pending': 'licenseType',
  'license.issuedOn': 'issuedOn',
  'license.expiresOn': 'expiresOn',
} as const satisfies Record<string, keyof ArquebusierValues>;
