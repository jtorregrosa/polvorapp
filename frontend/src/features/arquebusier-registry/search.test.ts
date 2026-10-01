import { describe, expect, it } from 'vitest';
import { matchesSearch, searchKey } from './search';
import { ROW_UNO } from './test-data';

const matches = (term: string) => matchesSearch(ROW_UNO, searchKey(term));

describe('matchesSearch', () => {
  it('finds a row by either name order, ignoring case and accents', () => {
    expect(matches(`${ROW_UNO.lastName.toUpperCase()} ${ROW_UNO.firstName}`)).toBe(true);
    expect(matches('garcia')).toBe(true);
  });

  it('finds a DNI/NIE typed with spaces, dots or hyphens', () => {
    const [digits, letter] = [ROW_UNO.nationalId.slice(0, -1), ROW_UNO.nationalId.slice(-1)];

    expect(matches(`${digits}-${letter.toLowerCase()}`)).toBe(true);
    expect(matches(`${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5)} ${letter}`)).toBe(true);
  });

  it('does not match everything when the term is only separators', () => {
    expect(matches(' - . ')).toBe(false);
  });

  it('finds a row by federation id', () => {
    expect(matches(String(ROW_UNO.federationId))).toBe(true);
    expect(matches('zzz')).toBe(false);
  });
});
