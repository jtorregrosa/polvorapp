import type { TFunction } from 'i18next';
import { describe, expect, it, vi } from 'vitest';
import { ApiProblemError } from '@/api/http';
import enCatalog from '@/i18n/locales/en/catalog.json';
import { applyFieldErrors, CATALOG_PROBLEM_CODES, messages, problemMessage } from './problems';

const t = ((key: string) => key) as unknown as TFunction<'catalog'>;

describe('catalogue problems', () => {
  it('has a translated message for every problem code the API returns', () => {
    const errors = enCatalog.errors as unknown as Record<string, Record<string, string>>;
    for (const code of CATALOG_PROBLEM_CODES) {
      const [area = '', name = ''] = code.split('.');
      expect(errors[area]?.[name], code).toBeTruthy();
    }
  });

  it('maps known codes, validation and anything else', () => {
    expect(problemMessage(t, new ApiProblemError(409, { status: 409, code: 'comparsas.nameTaken' }))).toBe(
      'errors.comparsas.nameTaken',
    );
    expect(problemMessage(t, new ApiProblemError(400, { status: 400, code: 'validation', errors: {} }))).toBe(
      'errors.validation',
    );
    expect(problemMessage(t, new ApiProblemError(500, undefined))).toBe('errors.generic');
    expect(problemMessage(t, new Error('network'))).toBe('errors.generic');
  });

  it('puts field errors on the named fields and reports unknown reasons as invalid', () => {
    const setError = vi.fn();
    const error = new ApiProblemError(400, {
      status: 400,
      code: 'validation',
      errors: { rentable: 'required', label: 'somethingNew', other: 'required' },
    });

    const applied = applyFieldErrors<{ rentable: boolean; label: string }>(
      error,
      ['rentable', 'label'],
      setError,
    );

    expect(applied).toBe(true);
    expect(setError).toHaveBeenCalledWith('rentable', {
      type: 'server',
      message: messages.required,
    });
    expect(setError).toHaveBeenCalledWith('label', { type: 'server', message: messages.invalid });
    expect(setError).toHaveBeenCalledTimes(2);
  });

  it('puts a conflict about one field on that field', () => {
    const setError = vi.fn();

    const applied = applyFieldErrors<{ name: string }>(
      new ApiProblemError(409, { status: 409, code: 'comparsas.nameTaken' }),
      ['name'],
      setError,
      { 'comparsas.nameTaken': 'name' },
    );

    expect(applied).toBe(true);
    expect(setError).toHaveBeenCalledWith('name', {
      type: 'server',
      message: 'catalog:errors.comparsas.nameTaken',
    });
  });

  it('applies nothing for other problems', () => {
    const setError = vi.fn();

    expect(
      applyFieldErrors<{ name: string }>(
        new ApiProblemError(409, { status: 409, code: 'comparsas.nameTaken' }),
        ['name'],
        setError,
      ),
    ).toBe(false);
    expect(setError).not.toHaveBeenCalled();
  });
});
