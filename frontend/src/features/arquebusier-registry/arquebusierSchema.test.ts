import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { INCOMPLETE_DATE } from '@/components/app/DateInput';
import { todayIso } from '@/lib/dates';
import {
  arquebusierSchema,
  EMPTY_ARQUEBUSIER,
  registerSchema,
  type ArquebusierValues,
} from './arquebusierSchema';
import { ownedWeaponSchema } from './ownedWeaponSchema';
import { messages, REGISTRY_PROBLEM_CODES } from './problems';

const VALID: ArquebusierValues = {
  ...EMPTY_ARQUEBUSIER,
  comparsaId: '00000000-0000-4000-8000-000000000001',
  federationId: '900001',
  nationalId: '00000001R',
  firstName: 'Arcabucera',
  lastName: 'Sintética',
  birthDate: '1990-05-01',
  gender: 'FEMALE',
  licenseType: 'AE',
  issuedOn: '2024-03-10',
  expiresOn: '2029-03-10',
};

const tomorrow = (() => {
  const date = new Date(`${todayIso()}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + 1);
  return date.toISOString().slice(0, 10);
})();

/** The messages the schema gives each field for `changes` applied to a valid arquebusier. */
function issues(changes: Partial<ArquebusierValues>, schema = arquebusierSchema): Record<string, string> {
  const result = schema.safeParse({ ...VALID, ...changes });
  return result.success
    ? {}
    : Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.'), issue.message]));
}

/**
 * Spec "Registry screens": the forms check the server's blocking rules before submitting (the
 * server's own are in RegistryInput and InputFields; the national id vectors are shared).
 */
describe('arquebusierSchema', () => {
  it('accepts a complete arquebusier, with or without optional data', () => {
    expect(issues({})).toEqual({});
    expect(issues({ licenseType: '', issuedOn: '', expiresOn: '', email: '', phone: '' })).toEqual({});
    expect(issues({ licensePending: true, issuedOn: '', expiresOn: '' })).toEqual({});
  });

  it.each<[string, Partial<ArquebusierValues>, string, string]>([
    ['an empty federation id', { federationId: ' ' }, 'federationId', messages.required],
    ['a federation id of 0', { federationId: '0' }, 'federationId', messages.federationId],
    ['a federation id over 9 digits', { federationId: '1000000000' }, 'federationId', messages.federationId],
    ['a federation id with letters', { federationId: '12a' }, 'federationId', messages.federationId],
    ['an empty name', { firstName: '  ' }, 'firstName', messages.required],
    ['a name over 100 characters', { lastName: 'a'.repeat(101) }, 'lastName', messages.tooLong],
    ['a control character in a name', { firstName: 'Ana\u0007' }, 'firstName', messages.invalid],
    ['a zero-width character in a name', { lastName: 'Sint​ética' }, 'lastName', messages.invalid],
    ['a missing birth date', { birthDate: '' }, 'birthDate', messages.required],
    ['a partly typed birth date', { birthDate: INCOMPLETE_DATE }, 'birthDate', messages.date],
    ['a birth date in the future', { birthDate: tomorrow }, 'birthDate', messages.future],
    ['a birth date before 1900', { birthDate: '1899-12-31' }, 'birthDate', messages.tooOld],
    [
      'a course date in the future',
      { trainingCompletedOn: tomorrow },
      'trainingCompletedOn',
      messages.future,
    ],
    ['an email over 254 characters', { email: `${'a'.repeat(250)}@x.es` }, 'email', messages.tooLong],
    ['an email without a domain dot', { email: 'ana@sintetica' }, 'email', messages.email],
    ['an email with non-ASCII letters', { email: 'añá@polvorapp.example' }, 'email', messages.email],
    ['a phone with letters', { phone: '600 ABC' }, 'phone', messages.phone],
    ['a phone that is only a plus', { phone: '+' }, 'phone', messages.phone],
    ['a phone over 20 characters', { phone: '+34 600 000 000 000 000' }, 'phone', messages.tooLong],
    ['no gender', { gender: '' }, 'gender', messages.choice],
    ['an unknown status', { status: 'GONE' }, 'status', messages.choice],
    ['a license without an issue date', { issuedOn: '' }, 'issuedOn', messages.required],
    [
      'an issue date in the future',
      { issuedOn: tomorrow, expiresOn: '2099-01-01' },
      'issuedOn',
      messages.future,
    ],
    ['an expiry on the issue date', { expiresOn: '2024-03-10' }, 'expiresOn', messages.notAfterIssued],
    ['an expiry before the issue date', { expiresOn: '2023-03-10' }, 'expiresOn', messages.notAfterIssued],
  ])('rejects %s', (_, changes, field, message) => {
    expect(issues(changes)[field]).toBe(message);
  });

  it('reports every broken rule in one pass', () => {
    expect(
      Object.keys(issues({ federationId: '', firstName: '', birthDate: '', gender: '' })).sort(),
    ).toEqual(['birthDate', 'federationId', 'firstName', 'gender']);
  });

  it('requires the comparsa only when registering', () => {
    expect(issues({ comparsaId: '' })).toEqual({});
    expect(issues({ comparsaId: '' }, registerSchema)).toEqual({ comparsaId: messages.choice });
  });
});

describe('ownedWeaponSchema', () => {
  const weapon = { weaponModelId: 'model', weaponNumber: '1001', ownershipGuideNumber: 'SINT-0001' };

  it('accepts a model and both numbers of up to 30 characters', () => {
    expect(ownedWeaponSchema.safeParse({ ...weapon, ownershipGuideNumber: 'G'.repeat(30) }).success).toBe(
      true,
    );
  });

  it.each([
    ['no model', { weaponModelId: '' }, messages.choice],
    ['an empty weapon number', { weaponNumber: '  ' }, messages.required],
    ['a guide number over 30 characters', { ownershipGuideNumber: 'G'.repeat(31) }, messages.tooLong],
  ])('rejects %s', (_, changes, message) => {
    const result = ownedWeaponSchema.safeParse({ ...weapon, ...changes });
    expect(result.success ? undefined : result.error.issues[0]?.message).toBe(message);
  });
});

describe('REGISTRY_PROBLEM_CODES', () => {
  it('lists exactly the problem codes the registry API defines, so none falls back to a generic text', () => {
    const source = readFileSync(
      resolve(
        dirname(fileURLToPath(import.meta.url)),
        '../../../../backend/src/Modules/ArquebusierRegistry/PolvorApp.ArquebusierRegistry/RegistryOutcome.cs',
      ),
      'utf8',
    );
    const apiCodes = [...source.matchAll(/public const string \w+ = "([\w.]+)";/g)].map((match) => match[1]);

    expect(apiCodes.length).toBeGreaterThan(0);
    expect([...REGISTRY_PROBLEM_CODES].sort()).toEqual(apiCodes.sort());
  });
});
