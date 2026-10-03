import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { I18nextProvider } from 'react-i18next';
import { describe, expect, it } from 'vitest';
import { createI18n } from '@/i18n';
import { useBillingText } from './billingText';
import { billingOf, EXAMPLE } from './test-data';

async function renderText(language: string) {
  window.localStorage.setItem('polvorapp.language', language);
  const i18n = await createI18n();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <I18nextProvider i18n={i18n}>{children}</I18nextProvider>
  );
  return renderHook(() => useBillingText(), { wrapper }).result.current;
}

/** Intl puts a non-breaking space before "€" in Spanish and Valencian. */
const plain = (text: string | null) => text?.replace(/\s/g, ' ') ?? null;

describe('useBillingText', () => {
  it('turns the lines into amount rows in Spanish', async () => {
    const text = await renderText('es-ES');

    const rows = text.rows(EXAMPLE).map((row) => ({
      ...row,
      unitPrice: plain(row.unitPrice),
      amount: plain(row.amount),
    }));

    expect(rows).toEqual([
      { id: 'POWDER', label: 'Pólvora', quantity: '5 kg', unitPrice: '55,00 €', amount: '275,00 €' },
      { id: 'CAPS', label: 'Pistones', quantity: '3 cajas', unitPrice: '4,50 €', amount: '13,50 €' },
      {
        id: 'WEAPON_RENTAL',
        label: 'Alquiler de arma',
        quantity: '2 alquileres',
        unitPrice: '30,00 €',
        amount: '60,00 €',
      },
      {
        id: 'FLASK_RENTAL',
        label: 'Alquiler de cantimplora',
        quantity: '2 cantimploras',
        unitPrice: '6,00 €',
        amount: '12,00 €',
      },
    ]);
    expect(plain(text.total(EXAMPLE))).toBe('360,50 €');
  });

  it('uses the singular for one unit', async () => {
    const text = await renderText('es-ES');

    const rows = text.rows(billingOf({ powderKg: 1, capsBoxes: 1, weaponRentals: 1, flaskRentals: 1 }));

    expect(rows.map((row) => row.quantity)).toEqual(['1 kg', '1 caja', '1 alquiler', '1 cantimplora']);
  });

  it('writes the amounts in Valencian and in English', async () => {
    const valencian = await renderText('ca-ES-valencia');
    expect(valencian.rows(EXAMPLE).map((row) => row.label)).toEqual([
      'Pólvora',
      'Pistons',
      "Lloguer d'arma",
      'Lloguer de cantimplora',
    ]);
    expect(plain(valencian.total(EXAMPLE))).toBe('360,50 €');

    const english = await renderText('en');
    expect(english.rows(EXAMPLE)[1]?.quantity).toBe('3 boxes');
    expect(english.total(EXAMPLE)).toBe('€360.50');
  });

  it('has no unit price, amount or total when a price is missing, and names it', async () => {
    const text = await renderText('es-ES');
    const summary = billingOf(
      { powderKg: 5, capsBoxes: 3, weaponRentals: 0, flaskRentals: 0 },
      'PROVISIONAL',
      ['CAPS'],
    );

    expect(text.rows(summary)[1]).toMatchObject({ quantity: '3 cajas', unitPrice: null, amount: null });
    expect(text.total(summary)).toBeNull();
    expect(text.missingPrices(summary)).toBe('Falta un precio en la edición: caja de pistones.');
  });

  it('names a missing price in Valencian, apostrophes included', async () => {
    const text = await renderText('ca-ES-valencia');
    const summary = billingOf(
      { powderKg: 0, capsBoxes: 0, weaponRentals: 1, flaskRentals: 1 },
      'PROVISIONAL',
      ['WEAPON_RENTAL'],
    );

    expect(text.rows(summary).map((row) => row.quantity)).toEqual([
      '0 kg',
      '0 caixes',
      '1 lloguer',
      '1 cantimplora',
    ]);
    expect(text.missingPrices(summary)).toBe("Falta un preu en l'edició: lloguer d'arma.");
  });

  it('names several missing prices as a list', async () => {
    const text = await renderText('en');
    const summary = billingOf(
      { powderKg: 1, capsBoxes: 0, weaponRentals: 0, flaskRentals: 1 },
      'PROVISIONAL',
      ['POWDER', 'FLASK_RENTAL'],
    );

    expect(text.missingPrices(summary)).toBe(
      'Prices are missing in the edition: powder per kg and flask rental.',
    );
  });
});
