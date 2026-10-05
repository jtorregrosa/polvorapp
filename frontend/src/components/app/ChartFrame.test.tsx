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

    const table = screen.getByRole('table', { name: 'Arcabuceros por edición' });
    expect(screen.queryByRole('button', { name: 'Mostrar tabla' })).not.toBeInTheDocument();
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Edición', 'En activo', 'Reserva']);
    const provisional = within(table).getByRole('rowheader', { name: '2031 (provisional)' });
    expect(
      within(provisional.closest('tr') as HTMLElement)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['1412', '30']);
    expect(within(table).getByRole('rowheader', { name: '2030' })).toBeInTheDocument();
  });

  it('hides the table behind a toggle under a drawing', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <ChartFrame {...props}>
        <p>dibujo</p>
      </ChartFrame>,
    );

    const toggle = screen.getByRole('button', { name: 'Mostrar tabla' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    await user.click(toggle);

    expect(screen.getByRole('button', { name: 'Ocultar tabla' })).toHaveAttribute('aria-expanded', 'true');
    const table = screen.getByRole('table', { name: 'Arcabuceros por edición' });
    expect(toggle).toHaveAttribute('aria-controls', table.closest('[id]:not(table)')?.id ?? 'missing');
    expect(within(table).getByRole('rowheader', { name: '2031 (provisional)' })).toBeInTheDocument();
  });

  it('formats the values with the given formatter', async () => {
    await renderWithProviders(
      <ChartFrame
        {...props}
        formatValue={(value) => `${String(value)} u.`}
        series={props.series.slice(0, 1)}
      />,
    );

    expect(screen.getByRole('cell', { name: '1412 u.' })).toBeInTheDocument();
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
