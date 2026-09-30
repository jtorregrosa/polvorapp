import { act, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { formatDate, formatNumber, intlLocale, useFormatters } from './format';

const lastDayOfSeptember = new Date(Date.UTC(2026, 8, 30, 10, 0, 0));

describe('intlLocale', () => {
  it.each([
    ['es-ES', 'es-ES'],
    ['ca-ES-valencia', 'ca-ES-valencia'],
    ['en', 'en-GB'],
    ['fr-FR', 'es-ES'],
  ])('formats %s with the %s conventions', (language, expected) => {
    expect(intlLocale(language)).toBe(expected);
  });
});

describe('formatDate', () => {
  it.each([
    ['es-ES', '30/9/2026'],
    ['ca-ES-valencia', '30/9/2026'],
    ['en', '30/09/2026'],
  ])('writes day before month in %s', (language, expected) => {
    expect(formatDate(lastDayOfSeptember, language)).toBe(expected);
  });

  it('uses the long month name of the language when asked', () => {
    expect(formatDate(lastDayOfSeptember, 'ca-ES-valencia', { dateStyle: 'long' })).toContain('setembre');
    expect(formatDate(lastDayOfSeptember, 'es-ES', { dateStyle: 'long' })).toContain('septiembre');
    expect(formatDate(lastDayOfSeptember, 'en', { dateStyle: 'long' })).toContain('September');
  });
});

describe('formatNumber', () => {
  it.each([
    ['es-ES', '1.234.567,5'],
    ['en', '1,234,567.5'],
  ])('uses the separators of %s', (language, expected) => {
    expect(formatNumber(1234567.5, language)).toBe(expected);
  });

  it('uses a comma as decimal separator in Valencian', () => {
    expect(formatNumber(2.5, 'ca-ES-valencia')).toBe('2,5');
  });
});

describe('useFormatters', () => {
  function Amount() {
    const format = useFormatters();
    return <p>{format.number(2.5)}</p>;
  }

  it('follows the active UI language', async () => {
    const { i18n } = await renderWithProviders(<Amount />, 'en');
    expect(screen.getByText('2.5')).toBeInTheDocument();

    await act(() => i18n.changeLanguage('es-ES'));

    expect(screen.getByText('2,5')).toBeInTheDocument();
  });
});
