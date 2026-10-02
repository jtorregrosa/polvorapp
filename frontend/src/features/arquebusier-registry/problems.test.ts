import i18next from 'i18next';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import { ApiProblemError } from '@/api/http';
import { resources } from '@/i18n';
import { applyFieldErrors, problemMessage } from './problems';

const t = i18next.getFixedT('es-ES', 'registry');

beforeAll(async () => {
  await i18next.init({ lng: 'es-ES', resources, ns: ['registry'], interpolation: { escapeValue: false } });
});

const failure = (status: number, code: string, errors?: Record<string, string>) =>
  new ApiProblemError(status, { status, title: 'Problem', code, ...(errors ? { errors } : {}) });

describe('problemMessage', () => {
  it('tells a FiringChief to contact the Federation and an Admin to search the registry', () => {
    const duplicate = failure(409, 'arquebusiers.nationalIdTaken');

    expect(problemMessage(t, duplicate)).toMatch(/contacta con la Federación/);
    expect(problemMessage(t, duplicate, { isAdmin: true })).toMatch(/Búscalo en el registro/);
  });

  it('explains throttling, busy rows, validation and anything else', () => {
    expect(problemMessage(t, new ApiProblemError(429, undefined))).toMatch(/muchos cambios seguidos/);
    expect(problemMessage(t, failure(503, 'registry.busy'))).toMatch(/ocupado/);
    expect(problemMessage(t, failure(400, 'validation'))).toBe('Revisa los campos con error.');
    expect(problemMessage(t, new Error('network'))).toBe('Algo ha fallado. Inténtalo de nuevo.');
  });
});

describe('applyFieldErrors', () => {
  it('puts each reason on its form field, license fields and model included', () => {
    const setError = vi.fn();
    const error = failure(400, 'validation', {
      nationalId: 'checkLetter',
      'license.issuedOn': 'future',
      weaponModelId: 'notFound',
    });

    const placed = applyFieldErrors(
      error,
      { nationalId: 'nationalId', 'license.issuedOn': 'issuedOn', weaponModelId: 'weaponModelId' },
      setError,
    );

    expect(placed).toBe(true);
    expect(setError.mock.calls).toEqual([
      ['nationalId', { type: 'server', message: 'registry:validation.checkLetter' }],
      ['issuedOn', { type: 'server', message: 'registry:validation.future' }],
      ['weaponModelId', { type: 'server', message: 'registry:validation.modelNotFound' }],
    ]);
  });

  it('says it placed nothing for sure when a reported field is not on the form, so the page adds its banner', () => {
    const setError = vi.fn();
    const error = failure(400, 'validation', {
      nationalId: 'checkLetter',
      constructor: 'invalid',
      other: 'invalid',
    });

    const placed = applyFieldErrors(error, { nationalId: 'nationalId' }, setError);

    expect(placed).toBe(false);
    expect(setError).toHaveBeenCalledTimes(1);
  });

  it('explains an invalid email, phone, federation id or date with the text the form itself uses', () => {
    const setError = vi.fn();
    const error = failure(400, 'validation', {
      email: 'invalid',
      phone: 'invalid',
      federationId: 'invalid',
      birthDate: 'invalid',
    });

    applyFieldErrors(
      error,
      { email: 'email', phone: 'phone', federationId: 'federationId', birthDate: 'birthDate' },
      setError,
    );

    expect(setError.mock.calls.map((call) => (call[1] as { message: string }).message)).toEqual([
      'registry:validation.email',
      'registry:validation.phone',
      'registry:validation.federationId',
      'registry:validation.date',
    ]);
  });

  it('puts a duplicate on its field with the role-specific text', () => {
    const setError = vi.fn();

    applyFieldErrors(failure(409, 'arquebusiers.federationIdTaken'), {}, setError, {
      isAdmin: true,
      conflicts: { 'arquebusiers.federationIdTaken': 'federationId' },
    });

    expect(setError).toHaveBeenCalledWith('federationId', {
      type: 'server',
      message: 'registry:errors.arquebusiers.federationIdTakenAdmin',
    });
  });

  it('places nothing for a problem that is not about fields', () => {
    expect(applyFieldErrors(failure(503, 'registry.busy'), {}, vi.fn())).toBe(false);
  });
});
