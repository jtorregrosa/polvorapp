import { describe, expect, it, vi } from 'vitest';
import { ApiProblemError } from '@/api/http';
import { ENTRY_FIELDS, type EntryValues } from './entrySchema';
import { applyFieldErrors, invalidEntries, needsReload, problemMessage } from './problems';

const refusal = (code: string, extra: Record<string, unknown> = {}) =>
  new ApiProblemError(409, { status: 409, title: 'Problem', code, ...extra });

describe('orders problems', () => {
  it('knows which refusals mean the order on screen is out of date', () => {
    expect(needsReload(refusal('orders.closed'))).toBe(true);
    expect(needsReload(refusal('entries.modified'))).toBe(true);
    expect(needsReload(refusal('orders.alreadyPrepared'))).toBe(false);
    expect(needsReload(new Error('network'))).toBe(false);
  });

  it('translates known codes and falls back for the rest', () => {
    const t = vi.fn((key: string) => key);

    expect(problemMessage(t as never, refusal('orders.validated'))).toBe('errors.orders.validated');
    expect(problemMessage(t as never, refusal('validation'))).toBe('errors.validation');
    expect(problemMessage(t as never, refusal('unknown.code'))).toBe('errors.generic');
  });

  it('reads the entries that block a submission, ignoring malformed ones', () => {
    const error = refusal('orders.entriesInvalid', {
      entries: [{ entryId: 'e-1', reasons: ['ownedWeaponMissing', 7] }, { entryId: 3 }, 'x', null],
    });

    expect(invalidEntries(error)).toEqual([{ entryId: 'e-1', reasons: ['ownedWeaponMissing'] }]);
    expect(invalidEntries(refusal('orders.closed'))).toEqual([]);
  });

  it('puts the API field errors on the panel fields, loan fields included', () => {
    const setError = vi.fn();
    const error = new ApiProblemError(400, {
      code: 'validation',
      errors: { 'loan.nationalId': 'lenderRegistered', loan: 'required', capsType: 'somethingNew' },
    });

    const applied = applyFieldErrors<EntryValues>(error, ENTRY_FIELDS, setError);

    expect(applied).toBe(true);
    expect(setError).toHaveBeenCalledWith('lenderNationalId', {
      type: 'server',
      message: 'orders:validation.lenderRegistered',
    });
    expect(setError).toHaveBeenCalledWith('lenderNationalId', {
      type: 'server',
      message: 'orders:validation.required',
    });
    expect(setError).toHaveBeenCalledWith('capsType', {
      type: 'server',
      message: 'orders:validation.invalid',
    });
    expect(applyFieldErrors<EntryValues>(refusal('orders.closed'), ENTRY_FIELDS, setError)).toBe(false);
  });
});
