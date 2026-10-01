import { describe, expect, it } from 'vitest';
import { isIsoDate, todayIso } from './dates';

describe('todayIso', () => {
  it.each([
    // Winter (UTC+1) and summer (UTC+2): late UTC evening is already tomorrow in Madrid.
    ['2026-01-14T22:59:59Z', '2026-01-14'],
    ['2026-01-14T23:30:00Z', '2026-01-15'],
    ['2026-07-14T21:59:59Z', '2026-07-14'],
    ['2026-07-14T22:00:00Z', '2026-07-15'],
    ['2026-12-31T23:00:00Z', '2027-01-01'],
  ])('at %s is %s, the date in Europe/Madrid as the API counts it', (now, expected) => {
    expect(todayIso(new Date(now))).toBe(expected);
  });
});

describe('isIsoDate', () => {
  it.each(['2024-03-10', '2024-02-29', '1900-01-01'])('accepts %s', (value) => {
    expect(isIsoDate(value)).toBe(true);
  });

  it.each(['', 'incomplete', '2023-02-29', '2024-13-01', '10/03/2024', '+012024-03-10', '20240-03-10'])(
    'rejects %s',
    (value) => {
      expect(isIsoDate(value)).toBe(false);
    },
  );
});
