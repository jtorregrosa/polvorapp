import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { ChartFrame, type ChartFrameProps } from './ChartFrame';

const props: ChartFrameProps = {
  title: 'Arcabuceros por edición',
  summary: '2031: 412 en activo, un 3 % más que en 2030.',
  note: 'Cifras sintéticas.',
  categoryLabel: 'Edición',
  series: [
    { key: 'active', label: 'En activo' },
    { key: 'reserve', label: 'Reserva' },
  ],
  data: [
    { id: '2030', label: '2030', values: { active: 1399, reserve: 44 } },
    { id: '2031', label: '2031', provisional: true, values: { active: 1412, reserve: 30 } },
  ],
};

function cellsOf(table: HTMLElement, rowHeader: string): string[] {
  const row = within(table).getByRole('rowheader', { name: rowHeader }).closest('tr');
  if (!row) throw new Error('No row');
  return within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent);
}

describe('ChartFrame', () => {
  it('names the chart with a heading, then its summary and note', async () => {
    await renderWithProviders(<ChartFrame {...props} />);

    const heading = screen.getByRole('heading', { level: 2, name: 'Arcabuceros por edición' });
    const region = heading.closest('section');
    if (!region) throw new Error('No section');
    expect(region).toHaveAccessibleName('Arcabuceros por edición');
    expect(within(region).getByText(props.summary)).toBeInTheDocument();
    expect(within(region).getByText('Cifras sintéticas.')).toBeInTheDocument();
  });

  it('shows the table directly without a drawing, with every value and the provisional row marked', async () => {
    await renderWithProviders(<ChartFrame {...props} />);

    const table = screen.getByRole('table', { name: 'Arcabuceros por edición: datos' });
    expect(screen.queryByRole('button', { name: /Tabla de datos/ })).not.toBeInTheDocument();
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Edición', 'En activo', 'Reserva']);
    expect(cellsOf(table, '2031 (provisional)')).toEqual(['1412', '30']);
    expect(within(table).getByRole('rowheader', { name: '2030' })).toBeInTheDocument();
  });

  it('hides the table behind a toggle named after the chart under a drawing', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <ChartFrame {...props}>
        <p>dibujo</p>
      </ChartFrame>,
    );

    const toggle = screen.getByRole('button', { name: 'Tabla de datos: Arcabuceros por edición' });
    expect(toggle).toHaveTextContent('Tabla de datos');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    await user.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const table = screen.getByRole('table', { name: 'Arcabuceros por edición: datos' });
    expect(document.getElementById(toggle.getAttribute('aria-controls') ?? '')).toContainElement(table);

    await user.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('formats values with the chart or series formatter, adds table-only series and shows no value as a dash', async () => {
    await renderWithProviders(
      <ChartFrame
        {...props}
        formatValue={(value) => `${String(value)} u.`}
        series={props.series.slice(0, 1)}
        tableSeries={[{ key: 'reserve', label: 'Reserva', formatValue: (value) => `R${String(value)}` }]}
        data={[...props.data, { id: '2032', label: '2032', values: { active: null } }]}
      />,
    );

    const table = screen.getByRole('table');
    expect(cellsOf(table, '2030')).toEqual(['1399 u.', 'R44']);
    expect(cellsOf(table, '2032')).toEqual(['—sin datos', '—sin datos']);
  });

  it.each(['es-ES', 'ca-ES-valencia', 'en'])('has no accessibility violations in %s', async (language) => {
    const user = userEvent.setup();
    const { container } = await renderWithProviders(
      <ChartFrame {...props}>
        <p>dibujo</p>
      </ChartFrame>,
      language,
    );
    await user.click(screen.getByRole('button', { expanded: false }));

    expect(await axeViolations(container)).toEqual([]);
  });
});
