import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { AmountTable } from './AmountTable';

const BILLING = {
  title: 'Resumen de pago',
  rows: [
    { id: 'POWDER', label: 'Pólvora', quantity: '5 kg', unitPrice: '55,00 €', amount: '275,00 €' },
    { id: 'CAPS', label: 'Pistones', quantity: '3 cajas', unitPrice: '4,50 €', amount: '13,50 €' },
  ],
  total: '288,50 €',
};

describe('AmountTable', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('names the table by its title and gives each line its quantity, unit price and amount', async () => {
    await renderWithProviders(<AmountTable {...BILLING} />);

    const table = screen.getByRole('table', { name: 'Resumen de pago' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['Concepto', 'Cantidad', 'Precio', 'Importe']);
    const row = within(table).getByRole('row', { name: /Pólvora/ });
    expect(within(row).getByRole('rowheader', { name: /Pólvora/ })).toHaveAttribute('scope', 'row');
    expect(within(row).getByRole('cell', { name: '5 kg' })).toBeInTheDocument();
    expect(within(row).getByRole('cell', { name: '55,00 €' })).toBeInTheDocument();
    expect(within(row).getByRole('cell', { name: '275,00 €' })).toBeInTheDocument();
  });

  it('puts the total in the footer as a row header', async () => {
    await renderWithProviders(<AmountTable {...BILLING} />);

    // Header, body and footer.
    const [, , footer] = screen.getAllByRole('rowgroup') as [HTMLElement, HTMLElement, HTMLElement];
    const total = within(footer).getByRole('rowheader', { name: 'Total' });
    expect(total).toHaveAttribute('scope', 'row');
    expect(within(footer).getByRole('cell', { name: '288,50 €' })).toBeInTheDocument();
  });

  it('has no footer without a total', async () => {
    await renderWithProviders(<AmountTable {...BILLING} total={null} />);

    expect(screen.getAllByRole('rowgroup')).toHaveLength(2);
    expect(screen.queryByRole('rowheader', { name: 'Total' })).not.toBeInTheDocument();
  });

  it('says "no price" in words where a price is missing', async () => {
    await renderWithProviders(
      <AmountTable
        {...BILLING}
        rows={[
          {
            id: 'FLASK_RENTAL',
            label: 'Alquiler de cantimplora',
            quantity: '2',
            unitPrice: null,
            amount: null,
          },
        ]}
        total={null}
      />,
    );

    const row = screen.getByRole('row', { name: /Alquiler de cantimplora/ });
    expect(within(row).getAllByRole('cell', { name: 'Sin precio' })).toHaveLength(2);
  });

  it('repeats quantity and unit price under the concept for narrow screens', async () => {
    await renderWithProviders(<AmountTable {...BILLING} />);

    // jsdom applies no CSS: which copy shows at each width is checked end to end at 360 px.
    expect(screen.getByRole('rowheader', { name: 'Pólvora 5 kg × 55,00 €' })).toBeInTheDocument();
  });

  it('translates its headers', async () => {
    await renderWithProviders(<AmountTable {...BILLING} />, 'en');

    expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual([
      'Item',
      'Quantity',
      'Price',
      'Amount',
    ]);
  });

  it.each(['light', 'dark'])('has no axe violations in the %s theme', async (theme) => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    const { container } = await renderWithProviders(<AmountTable {...BILLING} />);

    expect(await axeViolations(container)).toEqual([]);
  });
});
