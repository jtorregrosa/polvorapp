import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { billingOf, EXAMPLE } from '../test-data';
import { BillingSummarySection } from './BillingSummarySection';

/** Intl puts a non-breaking space before "€" in Spanish. */
const euros = (text: string) => new RegExp(`^${text.replace(' €', '\\s€')}$`);

describe('BillingSummarySection', () => {
  it('shows the four lines, the total and that the amount is provisional', async () => {
    await renderWithProviders(<BillingSummarySection billing={EXAMPLE} />);

    const section = screen.getByRole('region', { name: 'Resumen de pago' });
    const table = within(section).getByRole('table', { name: 'Resumen de pago' });
    for (const [concept, amount] of [
      ['Pólvora', '275,00 €'],
      ['Pistones', '13,50 €'],
      ['Alquiler de arma', '60,00 €'],
      ['Alquiler de cantimplora', '12,00 €'],
    ] as const) {
      const row = within(table).getByRole('row', { name: new RegExp(concept) });
      expect(within(row).getByRole('cell', { name: euros(amount) })).toBeInTheDocument();
    }
    expect(within(table).getByRole('cell', { name: euros('360,50 €') })).toBeInTheDocument();
    expect(within(section).getByText('Provisional')).toBeInTheDocument();
    expect(
      within(section).getByText('Provisional: el importe puede cambiar hasta que la Unión valide el pedido.'),
    ).toBeInTheDocument();
  });

  it('says when the amount is final', async () => {
    await renderWithProviders(<BillingSummarySection billing={{ ...EXAMPLE, state: 'FINAL' }} />);

    expect(screen.getByText('Definitivo')).toBeInTheDocument();
    expect(screen.getByText(/^Definitivo: la Unión ha validado el pedido/)).toBeInTheDocument();
  });

  it('names a missing price instead of the total', async () => {
    const billing = billingOf(
      { powderKg: 5, capsBoxes: 3, weaponRentals: 2, flaskRentals: 2 },
      'PROVISIONAL',
      ['CAPS'],
    );

    await renderWithProviders(<BillingSummarySection billing={billing} />);

    const caps = screen.getByRole('row', { name: /Pistones/ });
    // The unit price and the amount (the stacked line under the concept keeps the quantity only).
    expect(within(caps).getAllByRole('cell', { name: 'Sin precio' })).toHaveLength(2);
    expect(within(caps).getByRole('rowheader', { name: 'Pistones 3 cajas' })).toBeInTheDocument();
    expect(screen.queryByRole('rowheader', { name: 'Total' })).not.toBeInTheDocument();
    expect(
      screen.getByText(/^Falta un precio en la edición: caja de pistones\. No hay total hasta que la Unión/),
    ).toBeInTheDocument();
  });

  it('presents the edition billing with its own title and notes', async () => {
    await renderWithProviders(<BillingSummarySection billing={EXAMPLE} scope="edition" />, 'en');

    expect(screen.getByRole('region', { name: 'Edition billing summary' })).toBeInTheDocument();
    expect(screen.getByText('Provisional while any order is not validated.')).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: '€360.50' })).toBeInTheDocument();
  });

  it('says why the edition billing has no total', async () => {
    const billing = billingOf({ powderKg: 5, capsBoxes: 0, weaponRentals: 0, flaskRentals: 0 }, 'FINAL', [
      'POWDER',
    ]);

    await renderWithProviders(<BillingSummarySection billing={billing} scope="edition" />, 'en');

    expect(screen.getByText('Final: the Federation has validated every prepared order.')).toBeInTheDocument();
    expect(
      screen.getByText(
        'A price is missing in the edition: powder per kg. There is no total until the Federation sets the price in the edition.',
      ),
    ).toBeInTheDocument();
  });

  it('has no axe violations', async () => {
    const { container } = await renderWithProviders(<BillingSummarySection billing={EXAMPLE} />);

    expect(await axeViolations(container)).toEqual([]);
  });
});
