import { describe, expect, it } from 'vitest';
import { formatNumber } from './format';
import { moneyInputText, parseMoney } from './money';

describe('parseMoney', () => {
  it.each([
    ['4,50', 4.5],
    ['4.50', 4.5],
    ['4', 4],
    [' 55,00 ', 55],
    ['0', 0],
    ['0,01', 0.01],
    ['9999,99', 9999.99],
  ])('reads %s as %s euros', (text, value) => {
    expect(parseMoney(text)).toEqual({ kind: 'amount', value });
  });

  it('keeps the typed digits exactly, as JSON will send them', () => {
    const parsed = parseMoney('4,55');

    expect(parsed.kind === 'amount' && JSON.stringify(parsed.value)).toBe('4.55');
  });

  it.each([
    ['', 'empty'],
    ['   ', 'empty'],
    ['4,555', 'decimals'],
    ['10000', 'outOfRange'],
    ['-1', 'invalid'],
    ['1.000,50', 'invalid'],
    ['4,5,0', 'invalid'],
    ['cuatro', 'invalid'],
    ['4 €', 'invalid'],
  ])('reads %j as %s', (text, kind) => {
    expect(parseMoney(text).kind).toBe(kind);
  });
});

describe('moneyInputText', () => {
  it.each([
    ['es-ES', '4,50'],
    ['ca-ES-valencia', '4,50'],
    ['en', '4.50'],
  ])('writes an editable amount in %s', (language, expected) => {
    expect(moneyInputText(4.5, language)).toBe(expected);
  });

  it('writes no thousands separator, so the text reads back as the same amount', () => {
    const text = moneyInputText(1234.5, 'es-ES');

    expect(text).toBe('1234,50');
    expect(parseMoney(text)).toEqual({ kind: 'amount', value: 1234.5 });
  });

  it('is empty without an amount', () => {
    expect(moneyInputText(null, 'es-ES')).toBe('');
  });
});

describe('currency format', () => {
  it.each(['es-ES', 'ca-ES-valencia', 'en'])('writes euros with two decimals in %s', (language) => {
    const expected = new Intl.NumberFormat(language === 'en' ? 'en-GB' : language, {
      style: 'currency',
      currency: 'EUR',
    }).format(55);

    expect(formatNumber(55, language, { style: 'currency', currency: 'EUR' })).toBe(expected);
    expect(expected).toMatch(/55[.,]00/);
  });
});
